using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Exceptions;

namespace Aivsp.Infrastructure.Inference.Services;

public sealed class GeminiCliProcessRunner : IGeminiCliProcessRunner
{
    private const string SystemPromptFileName = "gemini-aivsp-system.md";
    private const string PolicyFileName = "gemini-aivsp-policy.toml";
    private const string SettingsFileName = "gemini-aivsp-settings.json";

    private static readonly Regex SafeModelNameRegex = new(
        @"^[A-Za-z0-9._:/-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<GeminiCliProcessResult> RunAsync(
        string executablePath,
        string prompt,
        string? modelName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InferenceUnavailableException("Gemini CLI executable path is empty.");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Gemini CLI prompt cannot be empty.", nameof(prompt));
        }

        if (!string.IsNullOrWhiteSpace(modelName) && !SafeModelNameRegex.IsMatch(modelName))
        {
            throw new ArgumentException("Gemini model name contains unsupported characters.", nameof(modelName));
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executablePath, modelName),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InferenceUnavailableException("Gemini CLI process could not be started.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new InferenceUnavailableException("Gemini CLI executable was not found or could not be started.", ex);
        }

        try
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);

            return new GeminiCliProcessResult(
                process.ExitCode,
                await standardOutputTask,
                await standardErrorTask);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, string? modelName)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        var systemPromptPath = Path.Combine(AppContext.BaseDirectory, SystemPromptFileName);
        var policyPath = Path.Combine(AppContext.BaseDirectory, PolicyFileName);
        var settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (!File.Exists(systemPromptPath) ||
            !File.Exists(policyPath) ||
            !File.Exists(settingsPath))
        {
            throw new InferenceUnavailableException(
                "AIVSP Gemini CLI isolation assets are missing.");
        }

        var arguments = BuildGeminiArguments(modelName, policyPath);
        var executableExtension = Path.GetExtension(executablePath);
        if (OperatingSystem.IsWindows() &&
            (executableExtension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                executableExtension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var command = BuildCommandScriptInvocation(executablePath, arguments);
            startInfo.Arguments = $"/d /s /c \"{command}\"";
        }
        else
        {
            startInfo.FileName = executablePath;
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        CopyUserEnvironmentVariable(startInfo, "GEMINI_API_KEY");
        CopyUserEnvironmentVariable(startInfo, "GOOGLE_API_KEY");
        CopyUserEnvironmentVariable(startInfo, "GOOGLE_CLOUD_PROJECT");
        CopyUserEnvironmentVariable(startInfo, "GOOGLE_GENAI_USE_VERTEXAI");
        CopyUserEnvironmentVariable(startInfo, "GOOGLE_APPLICATION_CREDENTIALS");
        startInfo.Environment["GEMINI_SYSTEM_MD"] = systemPromptPath;
        startInfo.Environment["GEMINI_CLI_SYSTEM_SETTINGS_PATH"] = settingsPath;
        startInfo.Environment["NO_COLOR"] = "1";

        return startInfo;
    }

    private static string[] BuildGeminiArguments(string? modelName, string policyPath)
    {
        var arguments = new System.Collections.Generic.List<string>
        {
            "--extensions",
            "none",
            "--skip-trust",
            "--prompt",
            "Procesa únicamente la solicitud recibida por entrada estándar.",
            "--output-format",
            "json",
            "--approval-mode",
            "default",
            "--admin-policy",
            policyPath
        };

        arguments.Add("--model");
        arguments.Add(
            string.IsNullOrWhiteSpace(modelName) ||
            string.Equals(
                modelName,
                AssistantOptions.DefaultGeminiModelName,
                StringComparison.OrdinalIgnoreCase)
                ? AssistantOptions.DefaultGeminiCliAlias
                : modelName);

        return arguments.ToArray();
    }

    private static string BuildCommandScriptInvocation(
        string executablePath,
        System.Collections.Generic.IEnumerable<string> arguments)
    {
        return string.Join(
            " ",
            new[] { QuoteCommandToken(executablePath) }
                .Concat(arguments.Select(QuoteCommandToken)));
    }

    private static string QuoteCommandToken(string value)
    {
        if (value.IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\r', '\n']) >= 0)
        {
            throw new ArgumentException(
                "Gemini CLI command contains unsupported command-shell characters.",
                nameof(value));
        }

        return $"\"{value}\"";
    }

    private static void CopyUserEnvironmentVariable(ProcessStartInfo startInfo, string name)
    {
        if (startInfo.Environment.TryGetValue(name, out var inheritedValue) &&
            !string.IsNullOrWhiteSpace(inheritedValue))
        {
            return;
        }

        var userValue = Environment.GetEnvironmentVariable(
            name,
            EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userValue))
        {
            startInfo.Environment[name] = userValue;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and Kill.
        }
        catch (Win32Exception)
        {
            // Cancellation remains the primary outcome even if Windows rejects Kill.
        }
    }
}

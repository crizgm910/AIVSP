using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aivsp.Application.Exceptions;

namespace Aivsp.Infrastructure.Inference.Services;

// Uses the same process contract as Gemini; the returned envelope is normalized,
// never raw prose from the CLI. Prompts travel over stdin, not command arguments.
public interface IAntigravityCliProcessRunner : IGeminiCliProcessRunner { }

public sealed class AntigravityCliProcessRunner : IAntigravityCliProcessRunner
{
    private const string Schema = """{"type":"object","properties":{"answer":{"type":"string"},"confidence":{"type":"number","minimum":0,"maximum":1}},"required":["answer","confidence"],"additionalProperties":false}""";
    private const int MaximumOutputCharacters = 1_048_576;

    public async Task<GeminiCliProcessResult> RunAsync(
        string executablePath, string prompt, string? modelName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Prompt is empty.", nameof(prompt));
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InferenceUnavailableException("Antigravity requires a native agy.exe executable.");
        if (string.IsNullOrWhiteSpace(modelName) || !Regex.IsMatch(modelName, @"\A[A-Za-z0-9._-]+\z"))
            throw new ArgumentException("Invalid Antigravity model name.", nameof(modelName));

        // The desktop may not inherit a recently updated PATH.
        if (executablePath.Equals("agy.exe", StringComparison.OrdinalIgnoreCase))
        {
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
            if (File.Exists(installed)) executablePath = installed;
        }

        var workspace = Path.Combine(Path.GetTempPath(), "AIVSP-AGY-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, ".agents", "agents"));
        try
        {
            CopyAsset("aivsp-text.md", Path.Combine(workspace, ".agents", "agents", "aivsp-text.md"));
            CopyAsset("hooks.json", Path.Combine(workspace, ".agents", "hooks.json"));
            CopyAsset("deny-tools.ps1", Path.Combine(workspace, ".agents", "deny-tools.ps1"));
            var hookPath = Path.Combine(workspace, ".agents", "hooks.json");
            var denyScript = File.ReadAllText(Path.Combine(workspace, ".agents", "deny-tools.ps1"));
            File.WriteAllText(hookPath, File.ReadAllText(hookPath).Replace(
                "__AIVSP_GATE_COMMAND__", Convert.ToBase64String(Encoding.Unicode.GetBytes(denyScript))));
            var info = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspace,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "--agent", "aivsp-text", "--model", modelName,
                "--input-format", "stream-json", "--output-format", "stream-json",
                "--json-schema", Schema, "--disable-slash-commands", "--print-timeout", "70s" })
                info.ArgumentList.Add(arg);
            info.Environment["NO_COLOR"] = "1";
            using var process = new Process { StartInfo = info };
            try
            {
                process.Start();
                var errors = ReadBoundedAsync(process.StandardError, process, cancellationToken);
                var output = ReadBoundedAsync(process.StandardOutput, process, cancellationToken);
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    @event = "user",
                    message = new { content = prompt }
                }).AsMemory(), cancellationToken);
                process.StandardInput.Close();
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, errors);
                if (process.ExitCode != 0)
                    throw new InferenceUnavailableException($"Antigravity CLI failed (exit {process.ExitCode}); check authentication, model availability or quota.");
                return new GeminiCliProcessResult(0, NormalizeOutput(await output), string.Empty);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                try { process.WaitForExit(3000); }
                catch (InvalidOperationException) { }
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (InferenceResponseException) { throw; }
        catch (InferenceUnavailableException) { throw; }
        catch (Exception ex)
        {
            throw new InferenceUnavailableException("Antigravity CLI could not complete the request.", ex);
        }
        finally
        {
            // Only the unique directory created above is removed, never user settings.
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string NormalizeOutput(string output)
    {
        try { return NormalizeOutputCore(output); }
        catch (JsonException ex)
        {
            throw new InferenceResponseException("Antigravity returned malformed JSON.", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            throw new InferenceResponseException("Antigravity returned an invalid event envelope.", ex);
        }
    }

    private static string NormalizeOutputCore(string output)
    {
        JsonElement? result = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InferenceResponseException("Antigravity event must be an object.");
            if (root.TryGetProperty("step_update", out var step) &&
                step.TryGetProperty("tool_name", out var tool) && tool.GetString() != "finish")
                throw new InferenceResponseException("Antigravity attempted a forbidden tool; answer rejected.");
            if (root.TryGetProperty("event", out var kind) && kind.GetString() == "result")
            {
                if (result != null) throw new InferenceResponseException("Duplicate Antigravity result.");
                result = root.GetProperty("result").Clone();
            }
        }
        if (result == null || !result.Value.TryGetProperty("status", out var status) || status.GetString() != "SUCCESS")
            throw new InferenceUnavailableException("Antigravity did not return a successful result.");
        if (!result.Value.TryGetProperty("structured_output", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(answer.GetString()) ||
            !data.TryGetProperty("confidence", out var confidence) || confidence.ValueKind != JsonValueKind.Number || !confidence.TryGetDouble(out var score) ||
            !double.IsFinite(score) || score is < 0 or > 1)
            throw new InferenceResponseException("Antigravity returned invalid structured output.");
        return JsonSerializer.Serialize(new { response = JsonSerializer.Serialize(new { answer = answer.GetString(), confidence = score }) });
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, Process process, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (result.Length + read > MaximumOutputCharacters)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                throw new InferenceResponseException("Antigravity output exceeded the safe size limit.");
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private static void CopyAsset(string name, string destination)
    {
        using var source = typeof(AntigravityCliProcessRunner).Assembly.GetManifestResourceStream(
            "Aivsp.Infrastructure.Inference.Assets.antigravity." + name)
            ?? throw new InferenceUnavailableException("Antigravity isolation asset is missing.");
        using var target = File.Create(destination);
        source.CopyTo(target);
    }
}

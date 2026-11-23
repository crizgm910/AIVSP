using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Exceptions;

namespace Aivsp.Infrastructure.Inference.Services;

public interface IAntigravityAcpClient
{
    Task<string> CompleteAsync(string prompt, IReadOnlyList<BrowserImageInput> images, AssistantOptions options, CancellationToken token);
}

public sealed class AntigravityAcpClient : IAntigravityAcpClient
{
    public static (string Model, string Effort) ResolveModel(string value)
    {
        var match = Regex.Match(value ?? "", @"\A(gemini-.+?)(?:-(low|medium|high|xhigh|max))?\z");
        if (!match.Success) throw new InferenceUnavailableException("ACP browser flow currently requires a Gemini model.");
        return (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : "low");
    }

    public async Task<string> CompleteAsync(string prompt, IReadOnlyList<BrowserImageInput> images, AssistantOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var (model, effort) = ResolveModel(options.AntigravityModelName);
        var directory = Path.Combine(Path.GetTempPath(), "AIVSP-ACP-" + Guid.NewGuid().ToString("N"));
        var agentDirectory = Path.Combine(directory, ".agents", "agents");
        Directory.CreateDirectory(agentDirectory);
        Process? process = null;
        try
        {
            string Asset(string name)
            {
                using var stream = typeof(AntigravityAcpClient).Assembly.GetManifestResourceStream("Aivsp.Infrastructure.Inference.Assets.antigravity." + name)
                    ?? throw new InferenceUnavailableException("ACP isolation assets are missing.");
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            await File.WriteAllTextAsync(Path.Combine(agentDirectory, "aivsp-browser.md"), Asset("aivsp-browser.md"), token);
            var hook = Asset("hooks.json").Replace("__AIVSP_GATE_COMMAND__", Convert.ToBase64String(Encoding.Unicode.GetBytes(Asset("deny-tools.ps1"))));
            await File.WriteAllTextAsync(Path.Combine(directory, ".agents", "hooks.json"), hook, token);
            var executable = options.AntigravityCliExecutablePath;
            if (executable.Equals("agy.exe", StringComparison.OrdinalIgnoreCase))
            {
                var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
                if (File.Exists(installed)) executable = installed;
            }
            if (!Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InferenceUnavailableException("ACP requires the native agy.exe executable.");
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "acp", "--stdio", "--agent", "aivsp-browser", "--disable-telemetry" }) start.ArgumentList.Add(arg);
            process = new Process { StartInfo = start }; process.Start();
            var errorDrain = DrainAsync(process.StandardError, token);
            var responseText = new StringBuilder();
            var nextId = 0;
            async Task<JsonElement> Rpc(string method, object parameters)
            {
                var id = ++nextId;
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }).AsMemory(), token);
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(token);
                    if (line == null) throw new InferenceUnavailableException("ACP closed its output stream.");
                    if (line.Length > 2_000_000) throw new InferenceResponseException("ACP event too large.");
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("method", out var methodProperty))
                    {
                        var name = methodProperty.GetString();
                        if (root.TryGetProperty("id", out var serverId))
                        {
                            object reply = name == "session/request_permission"
                                ? new { jsonrpc = "2.0", id = serverId.Clone(), result = (object)new { outcome = new { outcome = "cancelled" } } }
                                : new { jsonrpc = "2.0", id = serverId.Clone(), error = (object)new { code = -32601, message = "Capability denied" } };
                            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), token);
                            throw new InferenceResponseException("ACP requested a forbidden client capability.");
                        }
                        if (name == "session/update")
                        {
                            var update = root.GetProperty("params").GetProperty("update");
                            var kind = update.GetProperty("sessionUpdate").GetString();
                            if (kind is "tool_call" or "subagent_update") throw new InferenceResponseException("ACP attempted a forbidden tool or agent.");
                            if (kind == "agent_message_chunk" && update.TryGetProperty("content", out var content) &&
                                content.GetProperty("type").GetString() == "text")
                            {
                                responseText.Append(content.GetProperty("text").GetString());
                                if (responseText.Length > 32_000) throw new InferenceResponseException("ACP answer exceeds the allowed size.");
                            }
                        }
                    }
                    else if (root.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.Number && responseId.GetInt32() == id)
                    {
                        if (root.TryGetProperty("error", out var error))
                            throw new InferenceUnavailableException($"ACP protocol failed (code {error.GetProperty("code")}). Check authentication, model and quota.");
                        return root.GetProperty("result").Clone();
                    }
                }
            }
            var initialized = await Rpc("initialize", new
            {
                protocolVersion = 1,
                clientInfo = new { name = "aivsp-browser", version = "0.3.0" },
                clientCapabilities = new { fs = new { readTextFile = false, writeTextFile = false }, terminal = false }
            });
            if (initialized.GetProperty("protocolVersion").GetInt32() != 1 ||
                (images.Count > 0 && (!initialized.GetProperty("agentCapabilities").TryGetProperty("promptCapabilities", out var caps) ||
                    !caps.TryGetProperty("image", out var supported) || supported.ValueKind != JsonValueKind.True)))
                throw new InferenceUnavailableException("ACP does not support the required image protocol.");
            var session = await Rpc("session/new", new { cwd = directory, mcpServers = Array.Empty<object>() });
            var sessionId = session.GetProperty("sessionId").GetString();
            var configs = session.GetProperty("configOptions").EnumerateArray().ToArray();
            var modelConfig = configs.Single(config => config.GetProperty("id").GetString() == "model");
            var effortConfig = configs.Single(config => config.GetProperty("id").GetString() == "thought_level");
            if (!modelConfig.GetProperty("options").EnumerateArray().Any(item => item.GetProperty("value").GetString() == model) ||
                !effortConfig.GetProperty("options").EnumerateArray().Any(item => item.GetProperty("value").GetString() == effort))
                throw new InferenceUnavailableException("The configured ACP model or effort is unavailable; no fallback was used.");
            await Rpc("session/set_config_option", new { sessionId, configId = "model", value = model });
            await Rpc("session/set_config_option", new { sessionId, configId = "thought_level", value = effort });
            var blocks = new List<object> { new { type = "text", text = prompt } };
            blocks.AddRange(images.Select(image => (object)new { type = "image", mimeType = image.MimeType, data = image.Data }));
            var completed = await Rpc("session/prompt", new { sessionId, prompt = blocks });
            if (completed.GetProperty("stopReason").GetString() != "end_turn") throw new InferenceResponseException("ACP did not complete its answer.");
            token.ThrowIfCancellationRequested();
            return responseText.ToString();
        }
        catch (OperationCanceledException) { throw; }
        catch (InferenceResponseException) { throw; }
        catch (InferenceUnavailableException) { throw; }
        catch (Exception ex) { throw new InferenceUnavailableException("Antigravity ACP could not complete the browser request.", ex); }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { } }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}

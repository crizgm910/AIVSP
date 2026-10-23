using System.Text.Json;
using Aivsp.Application.Configuration;
using Aivsp.Application.Exceptions;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Aivsp.Infrastructure.Inference.Services;
using Moq;

namespace Aivsp.Infrastructure.Inference.Tests;

public class AntigravityCliTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("{\"event\":\"result\"}")]
    [InlineData("{\"event\":\"result\",\"result\":42}")]
    public void InvalidEnvelopeShape_IsRejected(string output) =>
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput(output));

    [Theory]
    [InlineData("finish", "allow")]
    [InlineData("read_file", "deny")]
    [InlineData("run_command", "deny")]
    [InlineData("invoke_subagent", "deny")]
    public async Task ToolGate_OnlyAllowsFinish(string tool, string expected)
    {
        using var asset = typeof(AntigravityCliProcessRunner).Assembly.GetManifestResourceStream(
            "Aivsp.Infrastructure.Inference.Assets.antigravity.deny-tools.ps1")!;
        using var reader = new StreamReader(asset);
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
                     Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(await reader.ReadToEndAsync())) })
            info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { toolCall = new { name = tool } }));
        process.StandardInput.Close();
        var response = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        using var document = JsonDocument.Parse(response);
        Assert.Equal(expected, document.RootElement.GetProperty("decision").GetString());
    }

    private static string Result(object data, string status = "SUCCESS") => JsonSerializer.Serialize(new
    {
        @event = "result",
        result = new { status, structured_output = data, response = "Ignore this raw prose" }
    });

    [Fact]
    public void StructuredOutput_IsPreferredOverProse()
    {
        var normalized = AntigravityCliProcessRunner.NormalizeOutput(Result(new { answer = "París", confidence = 0.9, toolAction = "finish" }));
        using var envelope = JsonDocument.Parse(normalized);
        using var answer = JsonDocument.Parse(envelope.RootElement.GetProperty("response").GetString()!);
        Assert.Equal("París", answer.RootElement.GetProperty("answer").GetString());
        Assert.False(answer.RootElement.TryGetProperty("toolAction", out _));
    }

    [Theory]
    [InlineData("TIMEOUT")]
    [InlineData("ERROR")]
    [InlineData("CANCELLED")]
    public void FailedStatus_IsNotAnAnswer(string status) =>
        Assert.Throws<InferenceUnavailableException>(() => AntigravityCliProcessRunner.NormalizeOutput(Result(new { answer = "No", confidence = 1 }, status)));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyAnswer_IsRejected(string answer) =>
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput(Result(new { answer, confidence = 0.9 })));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void OutOfRangeConfidence_IsRejected(double confidence) =>
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput(Result(new { answer = "París", confidence })));

    [Fact]
    public void MissingStructuredOutput_DoesNotFallBackToProse() =>
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput("""{"event":"result","result":{"status":"SUCCESS","response":"París"}}"""));

    [Fact]
    public void InvalidJson_IsRejected() =>
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput("not json"));

    [Fact]
    public void NonNumericConfidence_IsRejected() =>
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput(Result(new { answer = "París", confidence = "high" })));

    [Fact]
    public void DuplicateResult_IsRejected()
    {
        var line = Result(new { answer = "París", confidence = 0.9 });
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput(line + "\n" + line));
    }

    [Fact]
    public void ForbiddenTool_IsRejected()
    {
        var output = """{"event":"step_update","step_update":{"tool_name":"view_file"}}""" + "\n" + Result(new { answer = "París", confidence = 0.9 });
        Assert.Throws<InferenceResponseException>(() => AntigravityCliProcessRunner.NormalizeOutput(output));
    }

    [Fact]
    public async Task Service_UsesAntigravityPathAndModel_AndMapsOpenAnswer()
    {
        var runner = new Mock<IAntigravityCliProcessRunner>();
        runner.Setup(x => x.RunAsync("custom-agy.exe", It.IsAny<string>(), "gemini-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiCliProcessResult(0,
                AntigravityCliProcessRunner.NormalizeOutput(Result(new { answer = "París", confidence = 0.95 })), ""));
        var options = new AssistantOptions
        {
            InferenceProvider = InferenceProvider.AntigravityCli,
            AntigravityCliExecutablePath = "custom-agy.exe",
            AntigravityModelName = "gemini-test"
        };
        var detection = new AssistanceDetectionResult(AssistanceMode.OpenText,
            new List<OcrResult>
            {
                new("Escribe la capital de Francia.", new BoundingBox(20,20,300,30),1,OcrResultLevel.Line),
                new("Escribe tu respuesta", new BoundingBox(100,200,200,40),1,OcrResultLevel.Line)
            }, null!);
        using var http = new HttpClient(new NoNetworkHandler());
        var router = new InferenceServiceRouter(new OllamaInferenceService(http),
            new GeminiCliInferenceService(Mock.Of<IGeminiCliProcessRunner>()),
            new AntigravityCliInferenceService(runner.Object));
        var output = await router
            .InferActionAsync("Responde", detection, options, CancellationToken.None);
        Assert.Equal("París", Assert.IsType<TextSuggestionOutput>(output).SuggestedText);
        runner.VerifyAll();
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Ollama must not be called for Antigravity requests.");
    }

    [Fact]
    public async Task Runner_AlreadyCancelled_DoesNotLaunch() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AntigravityCliProcessRunner()
            .RunAsync("agy.exe", "prompt", AssistantOptions.DefaultAntigravityModelName, new CancellationToken(true)));

    [Theory]
    [InlineData("agy.cmd", "safe-model")]
    [InlineData("", "safe-model")]
    public async Task Runner_RejectsShellWrappers(string executable, string model) =>
        await Assert.ThrowsAsync<InferenceUnavailableException>(() => new AntigravityCliProcessRunner()
            .RunAsync(executable, "prompt", model, CancellationToken.None));

    [Theory]
    [InlineData("unsafe & command")]
    [InlineData("")]
    public async Task Runner_RejectsInvalidModels(string model) =>
        await Assert.ThrowsAsync<ArgumentException>(() => new AntigravityCliProcessRunner()
            .RunAsync("agy.exe", "prompt", model, CancellationToken.None));
}

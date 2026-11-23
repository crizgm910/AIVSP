using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Exceptions;
using Aivsp.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Aivsp.Application.Tests.BrowserIntegration;

public class AdvancedBrowserTests
{
    private readonly Mock<IInferenceService> _text = new(MockBehavior.Strict);
    private readonly Mock<IBrowserAdvancedInferenceService> _advanced = new();
    private readonly AssistantOptions _options = new() { InferenceProvider = InferenceProvider.AntigravityCli };
    private static BrowserQuestionRequest Request(bool multi = true, IReadOnlyList<BrowserImageInput>? images = null) =>
        new("context", AssistanceMode.MultipleChoice, "Selecciona los números primos.",
            new[] { new BrowserOptionInput("A", "2"), new BrowserOptionInput("B", "3"), new BrowserOptionInput("C", "4") }, null, images, multi);
    private BrowserDomAssistantService Service() => new(_text.Object, _options, NullLogger<BrowserDomAssistantService>.Instance, _advanced.Object);

    [Fact]
    public async Task MultiAnswer_UsesAdvancedAndPreservesAllKeys()
    {
        _advanced.Setup(x => x.InferAsync(It.IsAny<BrowserQuestionRequest>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserInferenceAnswer("2 y 3", new[] { "A", "B" }, 0.95));
        var result = await Service().AssistAsync(Request(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(new[] { "A", "B" }, result.OptionKeys); _text.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("A,A")]
    [InlineData("Z")]
    [InlineData("")]
    public async Task InvalidKeys_AreRejected(string keys)
    {
        _advanced.Setup(x => x.InferAsync(It.IsAny<BrowserQuestionRequest>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserInferenceAnswer("Primos", keys.Length == 0 ? Array.Empty<string>() : keys.Split(','), 0.9));
        await Assert.ThrowsAsync<InferenceResponseException>(() => Service().AssistAsync(Request(), Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData("A,A", 0.9, "duplicate_option_keys")]
    [InlineData("Z", 0.9, "unknown_option_key")]
    [InlineData("", 0.9, "no_option_selected")]
    [InlineData("", 0, "insufficient_context")]
    [InlineData("A", 2, "invalid_confidence")]
    public async Task Rejections_HaveSafeSpecificReason(string keys, double confidence, string reason)
    {
        _advanced.Setup(x => x.InferAsync(It.IsAny<BrowserQuestionRequest>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserInferenceAnswer("Contenido que no debe registrarse", keys.Length == 0 ? Array.Empty<string>() : keys.Split(','), confidence));
        var error = await Assert.ThrowsAsync<InferenceResponseException>(() => Service().AssistAsync(Request(), Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(reason, error.ReasonCode);
        Assert.DoesNotContain("Contenido", error.Message);
    }

    [Fact]
    public async Task OtherProvider_DoesNotSilentlyDropAdvancedContext()
    {
        _options.InferenceProvider = InferenceProvider.Ollama;
        await Assert.ThrowsAsync<InferenceUnavailableException>(() => Service().AssistAsync(Request(), Guid.NewGuid(), CancellationToken.None));
        _advanced.VerifyNoOtherCalls(); _text.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("text/html", "YWJj")]
    [InlineData("image/png", "not base64")]
    [InlineData("image/png", "YWJj")]
    public async Task InvalidImage_IsRejectedBeforeInference(string mime, string data) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service().AssistAsync(Request(false, new[] { new BrowserImageInput(mime, data) }), Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task SingleImageChoice_CannotReturnSeveralKeys()
    {
        var image = new BrowserImageInput("image/png", "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
        _advanced.Setup(x => x.InferAsync(It.IsAny<BrowserQuestionRequest>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserInferenceAnswer("Ambigua", new[] { "A", "B" }, 0.9));
        var error = await Assert.ThrowsAsync<InferenceResponseException>(() => Service().AssistAsync(Request(false, new[] { image }), Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("too_many_option_keys", error.ReasonCode);
    }
}

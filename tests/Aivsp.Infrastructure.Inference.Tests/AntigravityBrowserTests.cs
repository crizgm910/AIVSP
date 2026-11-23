using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Exceptions;
using Aivsp.Domain.Enums;
using Aivsp.Infrastructure.Inference.Services;
using Moq;

namespace Aivsp.Infrastructure.Inference.Tests;

public class AntigravityBrowserTests
{
    [Theory]
    [InlineData("gemini-3.8-flash-low", "gemini-3.8-flash", "low")]
    [InlineData("gemini-4-argon-medium", "gemini-4-argon", "medium")]
    [InlineData("gemini-3.8-flash", "gemini-3.8-flash", "low")]
    public void ModelAndEffort_AreExplicit(string value, string model, string effort) =>
        Assert.Equal((model, effort), AntigravityAcpClient.ResolveModel(value));

    [Fact]
    public void UnsupportedModel_IsNotSilentlyReplaced() => Assert.Throws<InferenceUnavailableException>(() => AntigravityAcpClient.ResolveModel("other-model"));

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"answer\":\"\",\"optionKeys\":[],\"confidence\":1}")]
    [InlineData("{\"answer\":\"ok\",\"optionKeys\":[],\"confidence\":2}")]
    [InlineData("{\"answer\":\"ok\",\"confidence\":1}")]
    public void InvalidJson_IsRejected(string json) => Assert.Throws<InferenceResponseException>(() => AntigravityBrowserInferenceService.Parse(json));

    [Fact]
    public void StructuredAnswer_PreservesMultipleKeys()
    {
        var result = AntigravityBrowserInferenceService.Parse("""{"answer":"Dos y tres","optionKeys":["A","B"],"confidence":0.95}""");
        Assert.Equal(new[] { "A", "B" }, result.OptionKeys);
    }

    [Fact]
    public async Task Image_IsPassedSeparately_NotWrittenIntoPrompt()
    {
        var client = new Mock<IAntigravityAcpClient>(); var options = new AssistantOptions();
        var images = new[] { new BrowserImageInput("image/png", "synthetic-pixels") };
        client.Setup(x => x.CompleteAsync(It.Is<string>(prompt => !prompt.Contains("synthetic-pixels")), images, options, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"answer":"Saturno","optionKeys":["A"],"confidence":1}""");
        var service = new AntigravityBrowserInferenceService(client.Object);
        await service.InferAsync(new BrowserQuestionRequest("test", AssistanceMode.MultipleChoice, "¿Centro?", new[] { new BrowserOptionInput("A", "Saturno"), new BrowserOptionInput("B", "Marte") }, null, images), options, CancellationToken.None);
        client.VerifyAll();
    }

    [Fact]
    public async Task AlreadyCancelled_DoesNotLaunch() => await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        new AntigravityAcpClient().CompleteAsync("test", Array.Empty<BrowserImageInput>(), new AssistantOptions(), new CancellationToken(true)));
}

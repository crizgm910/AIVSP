using Aivsp.Application.Configuration;

namespace Aivsp.Application.Tests.Configuration;

public class AssistantOptionsTests
{
    [Fact]
    public void Defaults_KeepAutoPasteContextAliveBeyondInferenceTimeout()
    {
        var options = new AssistantOptions();

        Assert.True(
            options.AutoPasteMaximumContextAgeSeconds > options.InferenceTimeoutSeconds,
            "The auto-paste context must remain valid through the full inference timeout.");
        Assert.True(
            options.GeminiAutoPasteMaximumContextAgeSeconds >
            options.GeminiInferenceTimeoutSeconds,
            "Gemini auto-paste context must remain valid through its longer timeout.");
    }

    [Fact]
    public void Defaults_UseStableLocalProviderAndVisibleOverlay()
    {
        var options = new AssistantOptions();

        Assert.Equal(InferenceProvider.Ollama, options.InferenceProvider);
        Assert.Equal(OverlayProfile.Visible, options.OverlayProfile);
        Assert.False(options.UseVisionModel);
    }
}

using Aivsp.Application.Configuration;

namespace Aivsp.Application.Tests.Configuration;

public class OverlayStyleResolverTests
{
    [Fact]
    public void Resolve_Visible_PreservesDiagnosticStyleAndRequestedDuration()
    {
        var options = new AssistantOptions
        {
            OverlayProfile = OverlayProfile.Visible
        };

        var style = OverlayStyleResolver.Resolve(options, 2_500);

        Assert.Equal(8, style.IndicatorBorderThickness);
        Assert.Equal(0x40, style.IndicatorFillAlpha);
        Assert.True(style.IndicatorShadowEnabled);
        Assert.Equal(16, style.SuggestionFontSize);
        Assert.Equal(2_500, style.DurationMilliseconds);
    }

    [Fact]
    public void Resolve_Subtle_UsesThinLowDistractionStyle()
    {
        var options = new AssistantOptions
        {
            OverlayProfile = OverlayProfile.Subtle,
            SubtleOverlayDurationMilliseconds = 850
        };

        var style = OverlayStyleResolver.Resolve(options, 9_000);

        Assert.Equal(1, style.IndicatorBorderThickness);
        Assert.True(style.IndicatorFillAlpha <= 0x0F);
        Assert.False(style.IndicatorShadowEnabled);
        Assert.False(style.SuggestionShadowEnabled);
        Assert.Equal(850, style.DurationMilliseconds);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(10_000, 2_000)]
    public void Resolve_Subtle_ClampsUnsafeDuration(int configured, int expected)
    {
        var options = new AssistantOptions
        {
            OverlayProfile = OverlayProfile.Subtle,
            SubtleOverlayDurationMilliseconds = configured
        };

        var style = OverlayStyleResolver.Resolve(options, 2_500);

        Assert.Equal(expected, style.DurationMilliseconds);
    }
}

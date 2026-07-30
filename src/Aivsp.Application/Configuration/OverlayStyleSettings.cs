using System;

namespace Aivsp.Application.Configuration;

public readonly record struct OverlayStyleSettings(
    double IndicatorBorderThickness,
    byte IndicatorFillAlpha,
    bool IndicatorShadowEnabled,
    double SuggestionBorderThickness,
    byte SuggestionBackgroundAlpha,
    bool SuggestionShadowEnabled,
    double SuggestionFontSize,
    double SuggestionPadding,
    double SuggestionMaximumWidth,
    int DurationMilliseconds);

public static class OverlayStyleResolver
{
    public static OverlayStyleSettings Resolve(
        AssistantOptions options,
        int requestedDurationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.OverlayProfile switch
        {
            OverlayProfile.Visible => new OverlayStyleSettings(
                IndicatorBorderThickness: 8,
                IndicatorFillAlpha: 0x40,
                IndicatorShadowEnabled: true,
                SuggestionBorderThickness: 2,
                SuggestionBackgroundAlpha: 0xD9,
                SuggestionShadowEnabled: true,
                SuggestionFontSize: 16,
                SuggestionPadding: 15,
                SuggestionMaximumWidth: 500,
                DurationMilliseconds: Math.Clamp(requestedDurationMilliseconds, 100, 10_000)),
            OverlayProfile.Subtle => new OverlayStyleSettings(
                IndicatorBorderThickness: 1,
                IndicatorFillAlpha: 0x0F,
                IndicatorShadowEnabled: false,
                SuggestionBorderThickness: 1,
                SuggestionBackgroundAlpha: 0x66,
                SuggestionShadowEnabled: false,
                SuggestionFontSize: 13,
                SuggestionPadding: 8,
                SuggestionMaximumWidth: 420,
                DurationMilliseconds: Math.Clamp(
                    options.SubtleOverlayDurationMilliseconds,
                    300,
                    2_000)),
            _ => throw new InvalidOperationException(
                $"Unsupported overlay profile: {options.OverlayProfile}.")
        };
    }
}

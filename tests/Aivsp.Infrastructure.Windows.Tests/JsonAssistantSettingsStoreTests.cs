using System;
using System.IO;
using Aivsp.Application.Configuration;
using Aivsp.Infrastructure.Windows.Services;

namespace Aivsp.Infrastructure.Windows.Tests;

public class JsonAssistantSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Aivsp.Settings.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AntigravitySettings_RoundTripWithoutChangingBrowserToken()
    {
        var store = new JsonAssistantSettingsStore(Path.Combine(_directory, "settings.json"));
        var options = new AssistantOptions
        {
            InferenceProvider = InferenceProvider.AntigravityCli,
            AntigravityCliExecutablePath = "C:/tools/agy.exe",
            AntigravityModelName = "gemini-test",
            BrowserBridgeToken = new string('B', 64)
        };
        store.Save(options);
        var loaded = new AssistantOptions();
        store.LoadInto(loaded);
        Assert.Equal(InferenceProvider.AntigravityCli, loaded.InferenceProvider);
        Assert.Equal(options.AntigravityCliExecutablePath, loaded.AntigravityCliExecutablePath);
        Assert.Equal(options.AntigravityModelName, loaded.AntigravityModelName);
        Assert.Equal(options.BrowserBridgeToken, loaded.BrowserBridgeToken);
    }

    [Fact]
    public void LegacySettings_KeepProviderAndAddAntigravityDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "legacy.json");
        File.WriteAllText(path, """{"InferenceProvider":1}""");
        var loaded = new AssistantOptions();
        new JsonAssistantSettingsStore(path).LoadInto(loaded);
        Assert.Equal(InferenceProvider.GeminiCli, loaded.InferenceProvider);
        Assert.Equal(AssistantOptions.DefaultAntigravityModelName, loaded.AntigravityModelName);
        Assert.Equal("agy.exe", loaded.AntigravityCliExecutablePath);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSupportedSettings()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonAssistantSettingsStore(path);
        var source = new AssistantOptions
        {
            InferenceProvider = InferenceProvider.GeminiCli,
            OverlayProfile = OverlayProfile.Subtle,
            GeminiModelName = "gemini-test",
            GeminiInferenceTimeoutSeconds = 93,
            BrowserGeminiInferenceTimeoutSeconds = 27,
            GeminiAutoPasteMaximumContextAgeSeconds = 101,
            AutoPasteEnabled = false,
            DesktopOcrEnabled = false,
            SubtleOverlayDurationMilliseconds = 750,
            BrowserIntegrationEnabled = true,
            BrowserBridgeToken = new string('A', 64)
        };

        store.Save(source);
        var loaded = new AssistantOptions();
        store.LoadInto(loaded);

        Assert.Equal(InferenceProvider.GeminiCli, loaded.InferenceProvider);
        Assert.Equal(OverlayProfile.Subtle, loaded.OverlayProfile);
        Assert.Equal("gemini-test", loaded.GeminiModelName);
        Assert.Equal(93, loaded.GeminiInferenceTimeoutSeconds);
        Assert.Equal(27, loaded.BrowserGeminiInferenceTimeoutSeconds);
        Assert.Equal(101, loaded.GeminiAutoPasteMaximumContextAgeSeconds);
        Assert.False(loaded.AutoPasteEnabled);
        Assert.False(loaded.DesktopOcrEnabled);
        Assert.Equal(750, loaded.SubtleOverlayDurationMilliseconds);
        Assert.True(loaded.BrowserIntegrationEnabled);
        Assert.Equal(new string('A', 64), loaded.BrowserBridgeToken);
    }

    [Fact]
    public void LoadInto_MissingFile_KeepsDefaults()
    {
        var options = new AssistantOptions();
        var store = new JsonAssistantSettingsStore(Path.Combine(_directory, "missing.json"));

        store.LoadInto(options);

        Assert.Equal(InferenceProvider.Ollama, options.InferenceProvider);
        Assert.Equal(OverlayProfile.Visible, options.OverlayProfile);
        Assert.True(options.DesktopOcrEnabled);
    }

    [Fact]
    public void LoadInto_EmptyLegacyGeminiModel_UsesStableDefault()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonAssistantSettingsStore(path);
        var legacy = new AssistantOptions
        {
            GeminiModelName = string.Empty
        };
        store.Save(legacy);
        var loaded = new AssistantOptions
        {
            GeminiModelName = "temporary-value"
        };

        store.LoadInto(loaded);

        Assert.Equal(AssistantOptions.DefaultGeminiModelName, loaded.GeminiModelName);
    }

    [Fact]
    public void LoadInto_InvalidJson_ThrowsWithoutChangingOptions()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ invalid");
        var options = new AssistantOptions();
        var store = new JsonAssistantSettingsStore(path);

        Assert.ThrowsAny<Exception>(() => store.LoadInto(options));
        Assert.Equal(InferenceProvider.Ollama, options.InferenceProvider);
    }

    [Fact]
    public void LoadInto_OutOfRangeDuration_ThrowsWithoutApplying()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "inferenceProvider": 1,
              "overlayProfile": 1,
              "modelName": "qwen3:4b",
              "ollamaEndpoint": "http://127.0.0.1:11434",
              "geminiCliExecutablePath": "gemini.cmd",
              "inferenceTimeoutSeconds": 30,
              "overlayDurationMilliseconds": 2500,
              "subtleOverlayDurationMilliseconds": 9999
            }
            """);
        var options = new AssistantOptions();
        var store = new JsonAssistantSettingsStore(path);

        Assert.Throws<InvalidDataException>(() => store.LoadInto(options));
        Assert.Equal(OverlayProfile.Visible, options.OverlayProfile);
    }

    [Fact]
    public void LoadInto_GeminiContextAgeNotGreaterThanTimeout_ThrowsWithoutApplying()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "inferenceProvider": 1,
              "overlayProfile": 1,
              "modelName": "qwen3:4b",
              "ollamaEndpoint": "http://127.0.0.1:11434",
              "geminiCliExecutablePath": "gemini.cmd",
              "inferenceTimeoutSeconds": 30,
              "geminiInferenceTimeoutSeconds": 75,
              "autoPasteMaximumContextAgeSeconds": 45,
              "geminiAutoPasteMaximumContextAgeSeconds": 75,
              "overlayDurationMilliseconds": 2500,
              "subtleOverlayDurationMilliseconds": 900
            }
            """);
        var options = new AssistantOptions();
        var store = new JsonAssistantSettingsStore(path);

        Assert.Throws<InvalidDataException>(() => store.LoadInto(options));
        Assert.Equal(75, options.GeminiInferenceTimeoutSeconds);
        Assert.Equal(85, options.GeminiAutoPasteMaximumContextAgeSeconds);
    }

    [Fact]
    public void LoadInto_UseVisionModelProperty_CannotEnableDisabledVlm()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "inferenceProvider": 0,
              "overlayProfile": 0,
              "modelName": "qwen3:4b",
              "ollamaEndpoint": "http://127.0.0.1:11434",
              "geminiCliExecutablePath": "gemini.cmd",
              "inferenceTimeoutSeconds": 30,
              "geminiInferenceTimeoutSeconds": 75,
              "autoPasteMaximumContextAgeSeconds": 35,
              "geminiAutoPasteMaximumContextAgeSeconds": 85,
              "overlayDurationMilliseconds": 2500,
              "subtleOverlayDurationMilliseconds": 900,
              "useVisionModel": true
            }
            """);
        var options = new AssistantOptions();
        var store = new JsonAssistantSettingsStore(path);

        store.LoadInto(options);

        Assert.False(options.UseVisionModel);
    }

    [Fact]
    public void LoadInto_InvalidBrowserBridgeToken_ThrowsWithoutApplying()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """
            {
              "inferenceProvider": 0,
              "overlayProfile": 0,
              "modelName": "qwen3:4b",
              "ollamaEndpoint": "http://127.0.0.1:11434",
              "geminiCliExecutablePath": "gemini.cmd",
              "inferenceTimeoutSeconds": 30,
              "geminiInferenceTimeoutSeconds": 75,
              "autoPasteMaximumContextAgeSeconds": 35,
              "geminiAutoPasteMaximumContextAgeSeconds": 85,
              "overlayDurationMilliseconds": 2500,
              "subtleOverlayDurationMilliseconds": 900,
              "browserIntegrationEnabled": true,
              "browserBridgeToken": "not-a-valid-token"
            }
            """);
        var options = new AssistantOptions();
        var originalToken = options.BrowserBridgeToken;
        var store = new JsonAssistantSettingsStore(path);

        Assert.Throws<InvalidDataException>(() => store.LoadInto(options));
        Assert.False(options.BrowserIntegrationEnabled);
        Assert.Equal(originalToken, options.BrowserBridgeToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

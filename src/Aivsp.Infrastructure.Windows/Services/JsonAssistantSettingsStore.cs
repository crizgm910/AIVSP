using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;

namespace Aivsp.Infrastructure.Windows.Services;

public sealed class JsonAssistantSettingsStore : IAssistantSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public JsonAssistantSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIVSP",
            "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public void LoadInto(AssistantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        var json = File.ReadAllText(_settingsPath);
        var persisted = JsonSerializer.Deserialize<PersistedAssistantSettings>(
            json,
            SerializerOptions) ?? throw new InvalidDataException(
                "AIVSP settings file contained no settings object.");

        Validate(persisted);
        persisted.ApplyTo(options);
    }

    public void Save(AssistantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var persisted = PersistedAssistantSettings.From(options);
        Validate(persisted);

        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("AIVSP settings path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(persisted, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    private static void Validate(PersistedAssistantSettings settings)
    {
        if (!Enum.IsDefined(settings.InferenceProvider))
        {
            throw new InvalidDataException("Unknown inference provider in AIVSP settings.");
        }
        if (!Enum.IsDefined(settings.OverlayProfile))
        {
            throw new InvalidDataException("Unknown overlay profile in AIVSP settings.");
        }
        if (settings.InferenceTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidDataException("Inference timeout must be between 1 and 300 seconds.");
        }
        if (settings.GeminiInferenceTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidDataException("Gemini timeout must be between 1 and 300 seconds.");
        }
        if (settings.BrowserGeminiInferenceTimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidDataException(
                "Browser Gemini timeout must be between 1 and 120 seconds.");
        }
        if (settings.AutoPasteMaximumContextAgeSeconds <= settings.InferenceTimeoutSeconds)
        {
            throw new InvalidDataException("Ollama auto-paste context age must exceed its timeout.");
        }
        if (settings.GeminiAutoPasteMaximumContextAgeSeconds <= settings.GeminiInferenceTimeoutSeconds)
        {
            throw new InvalidDataException("Gemini auto-paste context age must exceed its timeout.");
        }
        if (settings.OverlayDurationMilliseconds is < 100 or > 10_000)
        {
            throw new InvalidDataException("Visible overlay duration is out of range.");
        }
        if (settings.SubtleOverlayDurationMilliseconds is < 300 or > 2_000)
        {
            throw new InvalidDataException("Subtle overlay duration is out of range.");
        }
        if (!Uri.TryCreate(settings.OllamaEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("Ollama endpoint must be an absolute HTTP or HTTPS URL.");
        }
        if (string.IsNullOrWhiteSpace(settings.ModelName))
        {
            throw new InvalidDataException("Ollama model name cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(settings.GeminiCliExecutablePath))
        {
            throw new InvalidDataException("Gemini CLI executable path cannot be empty.");
        }
        if (!string.IsNullOrWhiteSpace(settings.BrowserBridgeToken) &&
            (settings.BrowserBridgeToken.Length != 64 ||
                !settings.BrowserBridgeToken.All(Uri.IsHexDigit)))
        {
            throw new InvalidDataException(
                "Browser bridge token must contain exactly 64 hexadecimal characters.");
        }
    }

    private sealed class PersistedAssistantSettings
    {
        public InferenceProvider InferenceProvider { get; set; }
        public OverlayProfile OverlayProfile { get; set; }
        public string ModelName { get; set; } = "qwen3:4b";
        public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
        public string GeminiCliExecutablePath { get; set; } = "gemini.cmd";
        public string GeminiModelName { get; set; } =
            AssistantOptions.DefaultGeminiModelName;
        public string OcrLanguage { get; set; } = "es-MX";
        public int InferenceTimeoutSeconds { get; set; } = 30;
        public int GeminiInferenceTimeoutSeconds { get; set; } = 75;
        public int BrowserGeminiInferenceTimeoutSeconds { get; set; } = 30;
        public int AutoPasteMaximumContextAgeSeconds { get; set; } = 35;
        public int GeminiAutoPasteMaximumContextAgeSeconds { get; set; } = 85;
        public int OverlayDurationMilliseconds { get; set; } = 2_500;
        public int SubtleOverlayDurationMilliseconds { get; set; } = 900;
        public bool AutoPasteEnabled { get; set; } = true;
        public bool DesktopOcrEnabled { get; set; } = true;
        public bool BrowserIntegrationEnabled { get; set; }
        public string? BrowserBridgeToken { get; set; }

        public static PersistedAssistantSettings From(AssistantOptions options) => new()
        {
            InferenceProvider = options.InferenceProvider,
            OverlayProfile = options.OverlayProfile,
            ModelName = options.ModelName,
            OllamaEndpoint = options.OllamaEndpoint,
            GeminiCliExecutablePath = options.GeminiCliExecutablePath,
            GeminiModelName = options.GeminiModelName,
            OcrLanguage = options.OcrLanguage,
            InferenceTimeoutSeconds = options.InferenceTimeoutSeconds,
            GeminiInferenceTimeoutSeconds = options.GeminiInferenceTimeoutSeconds,
            BrowserGeminiInferenceTimeoutSeconds =
                options.BrowserGeminiInferenceTimeoutSeconds,
            AutoPasteMaximumContextAgeSeconds = options.AutoPasteMaximumContextAgeSeconds,
            GeminiAutoPasteMaximumContextAgeSeconds =
                options.GeminiAutoPasteMaximumContextAgeSeconds,
            OverlayDurationMilliseconds = options.OverlayDurationMilliseconds,
            SubtleOverlayDurationMilliseconds = options.SubtleOverlayDurationMilliseconds,
            AutoPasteEnabled = options.AutoPasteEnabled,
            DesktopOcrEnabled = options.DesktopOcrEnabled,
            BrowserIntegrationEnabled = options.BrowserIntegrationEnabled,
            BrowserBridgeToken = options.BrowserBridgeToken
        };

        public void ApplyTo(AssistantOptions options)
        {
            options.InferenceProvider = InferenceProvider;
            options.OverlayProfile = OverlayProfile;
            options.ModelName = ModelName;
            options.OllamaEndpoint = OllamaEndpoint;
            options.GeminiCliExecutablePath = GeminiCliExecutablePath;
            options.GeminiModelName = string.IsNullOrWhiteSpace(GeminiModelName)
                ? AssistantOptions.DefaultGeminiModelName
                : GeminiModelName;
            options.OcrLanguage = OcrLanguage;
            options.InferenceTimeoutSeconds = InferenceTimeoutSeconds;
            options.GeminiInferenceTimeoutSeconds = GeminiInferenceTimeoutSeconds;
            options.BrowserGeminiInferenceTimeoutSeconds =
                BrowserGeminiInferenceTimeoutSeconds;
            options.AutoPasteMaximumContextAgeSeconds = AutoPasteMaximumContextAgeSeconds;
            options.GeminiAutoPasteMaximumContextAgeSeconds =
                GeminiAutoPasteMaximumContextAgeSeconds;
            options.OverlayDurationMilliseconds = OverlayDurationMilliseconds;
            options.SubtleOverlayDurationMilliseconds = SubtleOverlayDurationMilliseconds;
            options.AutoPasteEnabled = AutoPasteEnabled;
            options.DesktopOcrEnabled = DesktopOcrEnabled;
            options.BrowserIntegrationEnabled = BrowserIntegrationEnabled;
            if (!string.IsNullOrWhiteSpace(BrowserBridgeToken))
            {
                options.BrowserBridgeToken = BrowserBridgeToken;
            }
        }
    }
}

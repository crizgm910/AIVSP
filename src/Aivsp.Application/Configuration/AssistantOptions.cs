using System;
using System.Security.Cryptography;

namespace Aivsp.Application.Configuration;

public class AssistantOptions
{
    public const string DefaultGeminiModelName = "gemini-3.1-flash-lite";
    public const string DefaultGeminiCliAlias = "aivsp-fast";

    public string ActivationHotkey { get; set; } = "Ctrl+Shift+F9";
    public int InferenceTimeoutSeconds { get; set; } = 30;
    public int GeminiInferenceTimeoutSeconds { get; set; } = 75;
    public int BrowserGeminiInferenceTimeoutSeconds { get; set; } = 30;
    public int OverlayDurationMilliseconds { get; set; } = 2500;
    public int SubtleOverlayDurationMilliseconds { get; set; } = 900;
    public double MinimumMatchScore { get; set; } = 0.8;
    public double MinimumMatchMargin { get; set; } = 0.05;
    public double MinimumAutoPasteConfidence { get; set; } = 0.75;
    public int AutoPasteMaximumContextAgeSeconds { get; set; } = 35;
    public int GeminiAutoPasteMaximumContextAgeSeconds { get; set; } = 85;
    public bool UseVisionModel { get; set; } = false;
    public string OcrLanguage { get; set; } = "es-MX";
    public string ModelName { get; set; } = "qwen3:4b";
    public string LocalKnowledgeContext { get; set; } = string.Empty;
    public InferenceProvider InferenceProvider { get; set; } = InferenceProvider.Ollama;
    public string GeminiCliExecutablePath { get; set; } = "gemini.cmd";
    public string GeminiModelName { get; set; } = DefaultGeminiModelName;
    public OverlayProfile OverlayProfile { get; set; } = OverlayProfile.Visible;
    public bool DesktopOcrEnabled { get; set; } = true;
    public bool BrowserIntegrationEnabled { get; set; }
    public string BrowserBridgeToken { get; set; } =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // Runtime states
    public bool IsPaused { get; set; } = false;
    public bool AutoPasteEnabled { get; set; } = true;
    public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string ConcurrencyPolicy { get; set; } = "LatestRequestWins";
    public string[] ExcludedProcesses { get; set; } = Array.Empty<string>();
}

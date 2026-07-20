using System;

namespace Aivsp.Application.Configuration;

public class AssistantOptions
{
    public string ActivationHotkey { get; set; } = "Ctrl+Shift+F9";
    public int InferenceTimeoutSeconds { get; set; } = 30;
    public int OverlayDurationMilliseconds { get; set; } = 2500;
    public double MinimumMatchScore { get; set; } = 0.8;
    public double MinimumMatchMargin { get; set; } = 0.05;
    public bool UseVisionModel { get; set; } = false;
    public string OcrLanguage { get; set; } = "es-MX";
    public string ModelName { get; set; } = "llama3.2:3b";
    
    // Runtime states
    public bool IsPaused { get; set; } = false;
    public bool AutoPasteEnabled { get; set; } = true;
    public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string ConcurrencyPolicy { get; set; } = "LatestRequestWins";
    public string[] ExcludedProcesses { get; set; } = Array.Empty<string>();
}

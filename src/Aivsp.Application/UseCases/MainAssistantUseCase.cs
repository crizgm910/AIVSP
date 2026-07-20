using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Detection;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Aivsp.Application.UseCases;

public class MainAssistantUseCase
{
    private readonly IImageCaptureService _captureService;
    private readonly IOcrEngine _ocrEngine;
    private readonly IQuestionTypeDetector _questionTypeDetector;
    private readonly IInferenceService _inferenceService;
    private readonly IOverlayService _overlayService;
    private readonly IInputSimulationService _inputSimulator;
    private readonly AssistantOptions _options;
    private readonly ILogger<MainAssistantUseCase> _logger;

    public MainAssistantUseCase(
        IImageCaptureService captureService,
        IOcrEngine ocrEngine,
        IQuestionTypeDetector questionTypeDetector,
        IInferenceService inferenceService,
        IOverlayService overlayService,
        IInputSimulationService inputSimulator,
        AssistantOptions options,
        ILogger<MainAssistantUseCase> logger)
    {
        _captureService = captureService;
        _ocrEngine = ocrEngine;
        _questionTypeDetector = questionTypeDetector;
        _inferenceService = inferenceService;
        _overlayService = overlayService;
        _inputSimulator = inputSimulator;
        _options = options;
        _logger = logger;
    }

    public async Task ExecuteAsync(string userPrompt, int? cursorX = null, int? cursorY = null, CancellationToken cancellationToken = default)
    {
        if (_options.IsPaused)
        {
            _logger.LogInformation("El asistente está pausado. Se ignora la solicitud.");
            return;
        }

        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.InferenceTimeoutSeconds > 0)
        {
            operationCts.CancelAfter(TimeSpan.FromSeconds(_options.InferenceTimeoutSeconds));
        }
        var operationToken = operationCts.Token;

        operationToken.ThrowIfCancellationRequested();

        // Avoid capturing a previous answer marker and stop any old animation before
        // processing a new request.
        _overlayService.Clear();

        var totalSw = Stopwatch.StartNew();
        _logger.LogInformation("Activando asistente visual con prompt: {Prompt}", userPrompt);

        _logger.LogDebug("Capturando ventana...");
        using var capturedImage = await _captureService.CaptureActiveWindowAsync(operationToken);
        
        System.Collections.Generic.IReadOnlyList<OcrResult> ocrResults = new System.Collections.Generic.List<OcrResult>();

        if (!_options.UseVisionModel)
        {
            _logger.LogDebug("Ejecutando OCR...");
            ocrResults = await _ocrEngine.ExtractTextAsync(capturedImage, _options, operationToken);

            ocrResults = QuestionContextSelector.Select(ocrResults, cursorY);
            _logger.LogInformation("Contexto aislado a la tarjeta de pregunta ({LineCount} líneas).", ocrResults.Count);

            if (ocrResults.Count == 0)
            {
                _logger.LogWarning("No se detectó texto en la pantalla.");
                return;
            }
        }

        var detectionResult = _questionTypeDetector.Detect(ocrResults, capturedImage)
            with { CursorX = cursorX, CursorY = cursorY };
        _logger.LogInformation("Modo detectado: {Mode}", detectionResult.Mode);

        if (detectionResult.Mode == AssistanceMode.Unknown)
        {
            _logger.LogWarning("Modo desconocido, el asistente no tomará acción.");
            return;
        }

        _logger.LogInformation("Consultando a Ollama de forma local. ¡Esto puede tomar unos segundos...!");

        var inferenceOutput = await _inferenceService.InferActionAsync(userPrompt, detectionResult, _options, operationToken);
        operationToken.ThrowIfCancellationRequested();
        
        if (inferenceOutput is VisualIndicatorOutput visualOutput)
        {
            _logger.LogInformation("Mostrando indicador visual en '{Text}'", visualOutput.SelectedText);
            await _overlayService.DrawIndicatorAsync(visualOutput.Match, _options.OverlayDurationMilliseconds);
        }
        else if (inferenceOutput is TextSuggestionOutput textOutput)
        {
            _logger.LogInformation("Sugerencia generada: '{Text}'", textOutput.SuggestedText);
            
            if (_options.AutoPasteEnabled)
            {
                _logger.LogInformation("Intentando simular teclado para auto-pegar...");
                await _inputSimulator.SimulatePasteAsync(
                    textOutput.SuggestedText,
                    textOutput.SuggestedX,
                    textOutput.SuggestedY,
                    operationToken);
            }

            operationToken.ThrowIfCancellationRequested();
            
            // Still draw the indicator for visual feedback but maybe shorter or lighter (we'll just draw it normally)
            await _overlayService.DrawTextSuggestionAsync(textOutput, 2000); // Shorter duration since it's pasted
        }
        else
        {
            _logger.LogWarning("La respuesta de inferencia no produjo un output compatible.");
        }

        totalSw.Stop();
        _logger.LogInformation("Flujo completado en {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
    }
}

using System.Diagnostics;
using System.Text.Json;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Aivsp.Infrastructure.Inference.Services;

if (!args.Contains("--live"))
{
    Console.WriteLine("Use --live to send synthetic test questions to Antigravity (requires login and may consume quota).");
    return;
}

var options = new AssistantOptions { InferenceProvider = InferenceProvider.AntigravityCli };
var runner = new AntigravityCliProcessRunner();
var service = new AntigravityCliInferenceService(runner);
var cases = new[]
{
    ("Resuelve 12 + 8.", new[] { "20" }),
    ("Para una app de clínica médica, explica qué formato usarías para un logotipo escalable y fotografías detalladas: raster o vectorial, justificando cada elección.", new[] { "vector", "raster" }),
    ("¿Cuál es la diferencia entre UX y UI? Explica cómo la jerarquía visual y proximidad en la UI ayudan a la UX.", new[] { "UX", "UI" })
};

foreach (var (question, expected) in cases)
{
    await CheckAsync(question, AssistanceMode.OpenText, expected);
}
await CheckAsync("¿Qué principio define Clean Architecture?\nA) Dependencia fuerte de base de datos\nB) UI define negocio\nC) Dependencias apuntan hacia dentro\nD) Dependencias apuntan hacia fuera",
    AssistanceMode.MultipleChoice, new[] { "C)" });

// Cancellation must terminate the launched tree and leave no per-request workspace.
using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
try
{
    await runner.RunAsync("agy.exe", "Resuelve 11 + 3.", options.AntigravityModelName, cancel.Token);
    Console.Error.WriteLine("FAIL cancellation: process completed before cancellation could be exercised.");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException) { Console.WriteLine("PASS cancellation"); }

async Task CheckAsync(string question, AssistanceMode mode, string[] expected)
{
    var timer = Stopwatch.StartNew();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var lines = question.Split('\n').Select((line, i) => new OcrResult(line,
        new BoundingBox(20, 20 + i * 40, 800, 30), 1, OcrResultLevel.Line)).ToList();
    if (mode == AssistanceMode.OpenText)
        lines.Add(new OcrResult("Escribe tu respuesta", new BoundingBox(20, 300, 800, 40), 1, OcrResultLevel.Line));
    try
    {
        var result = await service.InferActionAsync("Responde", new AssistanceDetectionResult(mode, lines, null!), options, timeout.Token);
        var answer = result is TextSuggestionOutput text ? text.SuggestedText : ((VisualIndicatorOutput)result).SelectedText;
        var passed = expected.All(word => answer.Contains(word, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, elapsedMs = timer.ElapsedMilliseconds, answer }));
        if (!passed) Environment.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {timer.ElapsedMilliseconds}ms {ex.GetType().Name}");
        Environment.ExitCode = 1;
    }
}

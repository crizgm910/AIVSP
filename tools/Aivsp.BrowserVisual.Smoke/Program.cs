using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Infrastructure.Inference.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

if (args.Length != 2 || args[0] != "--live") { Console.WriteLine("Use --live <synthetic JPEG fixture>. Sends synthetic content to your authenticated Antigravity account."); return; }
var image = new BrowserImageInput("image/jpeg", Convert.ToBase64String(await File.ReadAllBytesAsync(args[1])));
var options = new AssistantOptions { InferenceProvider = InferenceProvider.AntigravityCli, BrowserGeminiInferenceTimeoutSeconds = 60 };
var advanced = new AntigravityBrowserInferenceService(new AntigravityAcpClient());
var service = new BrowserDomAssistantService(new NoTextInference(), options, NullLogger<BrowserDomAssistantService>.Instance, advanced);
var requests = new[]
{
    new BrowserQuestionRequest("visual-center", AssistanceMode.MultipleChoice, "En la imagen, ¿qué cuerpo celeste está en el centro de la cuadrícula?", new[] {
        new BrowserOptionInput("A", "Júpiter"), new BrowserOptionInput("B", "Urano"), new BrowserOptionInput("C", "Saturno"), new BrowserOptionInput("D", "Neptuno") }, null, new[] { image }),
    new BrowserQuestionRequest("visual-below", AssistanceMode.OpenText, "En la imagen, ¿qué cuerpo celeste está justo debajo del central? Responde solo el nombre.", Array.Empty<BrowserOptionInput>(), null, new[] { image }),
    new BrowserQuestionRequest("visual-top-row", AssistanceMode.MultipleChoice, "Selecciona TODOS los cuerpos celestes que se ven en la fila superior.", new[] {
        new BrowserOptionInput("A", "Sol"), new BrowserOptionInput("B", "Venus"), new BrowserOptionInput("C", "Júpiter"), new BrowserOptionInput("D", "Saturno"), new BrowserOptionInput("E", "Marte") }, null, new[] { image }, true),
    new BrowserQuestionRequest("text-multi", AssistanceMode.MultipleChoice, "Selecciona TODOS los números primos.", new[] {
        new BrowserOptionInput("A", "2"), new BrowserOptionInput("B", "3"), new BrowserOptionInput("C", "4"), new BrowserOptionInput("D", "5") }, null, null, true)
};
for (var i = 0; i < requests.Length; i++)
{
    var timer = Stopwatch.StartNew();
    var result = await service.AssistAsync(requests[i], Guid.NewGuid(), CancellationToken.None);
    var expected = i switch { 0 => "C", 2 => "A,B,C", 3 => "A,B,D", _ => "" };
    var passed = i == 1 ? result.Answer.Contains("Mercurio", StringComparison.OrdinalIgnoreCase) : string.Join(",", result.OptionKeys!.Order()) == expected;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {requests[i].ContextId} {timer.ElapsedMilliseconds}ms {result.Answer} keys={string.Join(",", result.OptionKeys ?? Array.Empty<string>())}");
    if (!passed) Environment.ExitCode = 1;
}
using var cancel = new CancellationTokenSource(700);
try { await advanced.InferAsync(requests[0], options, cancel.Token); Environment.ExitCode = 1; Console.WriteLine("FAIL cancellation not exercised"); }
catch (OperationCanceledException) { Console.WriteLine("PASS cancellation"); }

sealed class NoTextInference : IInferenceService
{
    public Task<AssistanceOutput> InferActionAsync(string prompt, AssistanceDetectionResult detection, AssistantOptions options, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Visual or multi-answer flow must not drop context into text-only inference.");
}

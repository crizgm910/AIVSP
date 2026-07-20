using System.Collections.Generic;
using Aivsp.Application.Detection;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Xunit;

namespace Aivsp.Application.Tests.Detection;

public class QuestionTypeDetectorTests
{
    private readonly QuestionTypeDetector _detector = new();

    [Fact]
    public void Detect_OptionsTakePriorityOverWordEscribe()
    {
        var results = new List<OcrResult>
        {
            Line("12. ¿Cómo se escribe el término correcto?"),
            Line("A) Intérprete lingüístico"),
            Line("B) Pingüino bilingüe"),
            Line("C) Intérprete lïngüistícó"),
            Line("D) Cüentacüentos bilingue")
        };

        var detection = _detector.Detect(results, null!);

        Assert.Equal(AssistanceMode.MultipleChoice, detection.Mode);
    }

    [Fact]
    public void Detect_MultilineOptionsRemainMultipleChoice()
    {
        var results = new List<OcrResult>
        {
            Line("13. Explica cuál opción representa la arquitectura."),
            Line("A) Microservicios distribuidos mediante Kubernetes,"),
            Line("con despliegues blue-green."),
            Line("B) Arquitectura monolítica con procedimientos almacenados"),
            Line("y lógica en disparadores.")
        };

        var detection = _detector.Detect(results, null!);

        Assert.Equal(AssistanceMode.MultipleChoice, detection.Mode);
    }

    [Fact]
    public void Detect_OpenQuestionWithoutOptionsRemainsOpenText()
    {
        var results = new List<OcrResult>
        {
            Line("9. Explica brevemente por qué deben liberarse los handles GDI."),
            Line("Explica brevemente...")
        };

        var detection = _detector.Detect(results, null!);

        Assert.Equal(AssistanceMode.OpenText, detection.Mode);
    }

    private static OcrResult Line(string text) =>
        new(text, new BoundingBox(0, 0, 300, 24), 1, OcrResultLevel.Line);
}

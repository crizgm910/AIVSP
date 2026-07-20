using System.Collections.Generic;
using Aivsp.Application.Detection;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Xunit;

namespace Aivsp.Application.Tests.Detection;

public class QuestionContextSelectorTests
{
    [Fact]
    public void Select_ReturnsOnlyQuestionCardUnderCursor()
    {
        var results = new List<OcrResult>
        {
            Line("1. Primera pregunta", 100),
            Line("A) Incorrecta", 130),
            Line("B) Correcta", 160),
            Line("2. Segunda pregunta", 300),
            Line("A) Otra respuesta", 330)
        };

        var selected = QuestionContextSelector.Select(results, 110);

        Assert.Collection(selected,
            item => Assert.Equal("1. Primera pregunta", item.Text),
            item => Assert.Equal("A) Incorrecta", item.Text),
            item => Assert.Equal("B) Correcta", item.Text));
    }

    [Fact]
    public void Select_WhenCursorIsOnOption_KeepsItsQuestionCard()
    {
        var results = new List<OcrResult>
        {
            Line("6. Pregunta anterior", 50),
            Line("7. Pregunta abierta", 200),
            Line("Respuesta corta", 250),
            Line("8. Pregunta siguiente", 400)
        };

        var selected = QuestionContextSelector.Select(results, 255);

        Assert.Equal(2, selected.Count);
        Assert.Equal("7. Pregunta abierta", selected[0].Text);
        Assert.Equal("Respuesta corta", selected[1].Text);
    }

    private static OcrResult Line(string text, int y) =>
        new(text, new BoundingBox(100, y, 300, 24), 0.95, OcrResultLevel.Line);
}

using System.Collections.Generic;
using Xunit;
using Aivsp.Application.Matching;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;
using Aivsp.Domain.ValueObjects;
using Aivsp.Domain.Enums;
using System;

namespace Aivsp.Application.Tests.Matching;

public class CompositeTextMatcherTests
{
    [Fact]
    public void FindBestMatch_ExactMatch_ReturnsMatch()
    {
        var matcher = new CompositeTextMatcher();
        var options = new AssistantOptions { MinimumMatchScore = 0.8, MinimumMatchMargin = 0.05 };
        var inference = new InferenceResult("raw", "Guardar", 1.0, TimeSpan.Zero);
        
        var candidates = new List<OcrResult>
        {
            new OcrResult("Cancelar", new BoundingBox(0, 0, 10, 10), 1.0, OcrResultLevel.Word),
            new OcrResult("Guardar", new BoundingBox(10, 10, 10, 10), 1.0, OcrResultLevel.Word)
        };

        var match = matcher.FindBestMatch(inference, candidates, options);

        Assert.NotNull(match);
        Assert.Equal("Guardar", match.Candidate.Text);
        Assert.Equal(1.0, match.Score);
    }

    [Fact]
    public void FindBestMatch_FuzzyMatchWithPunctuation_ReturnsMatch()
    {
        var matcher = new CompositeTextMatcher();
        var options = new AssistantOptions { MinimumMatchScore = 0.8, MinimumMatchMargin = 0.05 };
        var inference = new InferenceResult("raw", "Submit File", 1.0, TimeSpan.Zero);
        
        var candidates = new List<OcrResult>
        {
            new OcrResult("> Submit   File...", new BoundingBox(10, 10, 10, 10), 1.0, OcrResultLevel.Word)
        };

        var match = matcher.FindBestMatch(inference, candidates, options);

        Assert.NotNull(match);
        Assert.Equal("> Submit   File...", match.Candidate.Text);
        Assert.Equal(1.0, match.Score); // Since spaces and punctuation are removed, it's exact 1.0
    }

    [Fact]
    public void FindBestMatch_TinyMargin_ReturnsNull()
    {
        var matcher = new CompositeTextMatcher();
        var options = new AssistantOptions { MinimumMatchScore = 0.8, MinimumMatchMargin = 0.4 };
        var inference = new InferenceResult("raw", "Ok", 1.0, TimeSpan.Zero);
        
        var candidates = new List<OcrResult>
        {
            new OcrResult("Ok", new BoundingBox(0, 0, 10, 10), 1.0, OcrResultLevel.Word),
            new OcrResult("Oka", new BoundingBox(10, 10, 10, 10), 1.0, OcrResultLevel.Word) // Ok -> Oka = 0.66 similarity. Margin = 0.33
        };

        var match = matcher.FindBestMatch(inference, candidates, options);

        Assert.Null(match); // Margin 0.33 < 0.4
    }
}

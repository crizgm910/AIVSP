namespace Aivsp.Domain.Entities;

public abstract class AssistanceOutput
{
    public string RawResponse { get; }
    public double Confidence { get; }
    public System.TimeSpan Duration { get; }

    protected AssistanceOutput(string rawResponse, double confidence, System.TimeSpan duration)
    {
        RawResponse = rawResponse;
        Confidence = confidence;
        Duration = duration;
    }
}

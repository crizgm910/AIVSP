using System;

namespace Aivsp.Application.Exceptions;

public sealed class InferenceResponseException : Exception
{
    public InferenceResponseException(string message)
        : base(message)
    {
    }

    public InferenceResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

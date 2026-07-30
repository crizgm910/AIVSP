using System;

namespace Aivsp.Application.Exceptions;

public sealed class InferenceModelUnavailableException : Exception
{
    public InferenceModelUnavailableException(string message)
        : base(message)
    {
    }
}

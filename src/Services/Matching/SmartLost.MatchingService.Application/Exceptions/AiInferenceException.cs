namespace SmartLost.MatchingService.Application.Exceptions;

/// <summary>Raised by AI adapters for unavailable runtimes or invalid model output; messages must not contain photo data.</summary>
public sealed class AiInferenceException : Exception
{
    public AiInferenceException()
    {
    }

    public AiInferenceException(string message) : base(message)
    {
    }

    public AiInferenceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

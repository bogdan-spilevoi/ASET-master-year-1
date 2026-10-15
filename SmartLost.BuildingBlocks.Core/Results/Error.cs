using System.Diagnostics.CodeAnalysis;

namespace SmartLost.BuildingBlocks.Core.Results;

[SuppressMessage("Naming", "CA1716", Justification = "Error is the deliberate name of the C# result contract.")]
public sealed record Error(string Code, string Message, ErrorKind Kind, string? PropertyName = null);

public enum ErrorKind
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}

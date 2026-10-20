using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Core.Validation;

/// <summary>Checks domain invariants using a caller-supplied business error.</summary>
public static class Guard
{
    public static void NotNull(object? value, Error error)
    {
        Against(value is null, error);
    }

    public static void NotNullOrWhiteSpace(string? value, Error error, int? maximumLength = null)
    {
        if (maximumLength.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumLength.Value);
        }

        Against(string.IsNullOrWhiteSpace(value) ||
            (maximumLength.HasValue && value.Length > maximumLength.Value), error);
    }

    public static void InRange(int value, int minimum, int maximum, Error error)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        Against(value < minimum || value > maximum, error);
    }

    public static void Positive(decimal value, Error error)
    {
        Against(value <= 0, error);
    }

    public static void GreaterThan(decimal value, decimal bound, Error error)
    {
        Against(value <= bound, error);
    }

    public static void NotNegative(decimal value, Error error)
    {
        Against(value < 0, error);
    }

    public static void Against(bool invalid, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (invalid)
        {
            throw new DomainException(error);
        }
    }
}

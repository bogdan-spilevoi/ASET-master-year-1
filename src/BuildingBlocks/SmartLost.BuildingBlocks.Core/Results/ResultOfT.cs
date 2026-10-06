using System.Diagnostics.CodeAnalysis;

namespace SmartLost.BuildingBlocks.Core.Results;

[SuppressMessage("Design", "CA1000", Justification = "Typed factories implement the static result contract used by pipeline behaviors.")]
public sealed class Result<T> : IResult<Result<T>>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        _value = value;
        Errors = errors;
    }

    public bool IsSuccess
    {
        get;
    }

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<Error> Errors
    {
        get;
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failure has no value.");

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, value, []);
    }

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Failure([error]);
    }

    public static Result<T> Failure(IReadOnlyList<Error> errors)
    {
        return new Result<T>(false, default, Result.CopyErrors(errors));
    }
}

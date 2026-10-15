namespace SmartLost.BuildingBlocks.Core.Results;

public sealed class Result : IResult<Result>
{
    private Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
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

    public static Result Success()
    {
        return new Result(true, []);
    }

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Failure([error]);
    }

    public static Result Failure(IReadOnlyList<Error> errors)
    {
        return new Result(false, CopyErrors(errors));
    }

    internal static IReadOnlyList<Error> CopyErrors(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0 || errors.Any(error => error is null))
        {
            throw new ArgumentException("A failure must contain at least one non-null error.", nameof(errors));
        }

        return Array.AsReadOnly(errors.ToArray());
    }
}

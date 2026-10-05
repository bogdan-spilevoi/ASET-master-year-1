namespace SmartLost.BuildingBlocks.Core.Results;

public interface IResult
{
    bool IsSuccess
    {
        get;
    }

    bool IsFailure
    {
        get;
    }

    IReadOnlyList<Error> Errors
    {
        get;
    }
}

public interface IResult<TSelf> : IResult where TSelf : IResult<TSelf>
{
    static abstract TSelf Failure(IReadOnlyList<Error> errors);
}

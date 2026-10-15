using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Core.Exceptions;

public sealed class DomainException : Exception
{
    public DomainException(Error error) : base(error.Message)
    {
        Error = error;
    }

    public Error Error
    {
        get;
    }
}

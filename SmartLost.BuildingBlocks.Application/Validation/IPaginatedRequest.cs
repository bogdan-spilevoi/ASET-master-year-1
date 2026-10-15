namespace SmartLost.BuildingBlocks.Application.Validation;

public interface IPaginatedRequest
{
    int PageIndex
    {
        get;
    }

    int PageSize
    {
        get;
    }
}

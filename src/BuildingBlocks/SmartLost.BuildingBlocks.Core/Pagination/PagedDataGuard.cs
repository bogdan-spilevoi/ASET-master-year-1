namespace SmartLost.BuildingBlocks.Core.Pagination;

public static class PagedDataGuard
{
    public const int DefaultPageSize = 10;
    public const int MaximumPageSize = 100;

    /// <summary>Normalizes paging for callers that explicitly opt into repository defaults.</summary>
    public static void Clamp(ref int pageIndex, ref int pageSize)
    {
        pageIndex = Math.Max(0, pageIndex);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaximumPageSize);
    }
}

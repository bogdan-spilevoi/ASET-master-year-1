namespace SmartLost.BuildingBlocks.Core.Pagination;

public sealed class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int totalCount, int currentPage, int pageSize)
        : this(new PageSlice<T>(items, totalCount), currentPage, pageSize)
    {
    }

    public PagedResult(PageSlice<T> page, int currentPage, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegative(currentPage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, PagedDataGuard.MaximumPageSize);
        if (page.Items.Count > pageSize)
        {
            throw new ArgumentException("The returned item count cannot exceed the page size.", nameof(page));
        }

        Items = page.Items;
        TotalCount = page.TotalCount;
        CurrentPage = currentPage;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items
    {
        get;
    }

    public int TotalCount
    {
        get;
    }

    /// <summary>The zero-based page index, matching IPaginatedRequest.PageIndex.</summary>
    public int CurrentPage
    {
        get;
    }

    public int PageSize
    {
        get;
    }
}

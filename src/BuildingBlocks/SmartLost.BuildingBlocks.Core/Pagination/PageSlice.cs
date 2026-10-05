namespace SmartLost.BuildingBlocks.Core.Pagination;

public sealed class PageSlice<T>
{
    public PageSlice(IReadOnlyList<T> items, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        Items = Array.AsReadOnly(items.ToArray());
        if (Items.Count > totalCount)
        {
            throw new ArgumentException("Total count cannot be smaller than the returned item count.", nameof(totalCount));
        }

        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items
    {
        get;
    }

    public int TotalCount
    {
        get;
    }

    public PageSlice<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new PageSlice<TResult>([.. Items.Select(selector)], TotalCount);
    }
}

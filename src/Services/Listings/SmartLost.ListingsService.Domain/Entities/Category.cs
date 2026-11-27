using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class Category : AggregateRoot<Guid>
{
    private Category()
    {
    }

    public string NormalizedName { get; private set; } = string.Empty;

    public string? BroadClass
    {
        get; private set;
    }

    public Guid? MergedIntoId
    {
        get; private set;
    }

    public static Category Create(string name)
    {
        return new Category { Id = Guid.NewGuid(), NormalizedName = NormalizeName(name) };
    }

    public void Rename(string name)
    {
        NormalizedName = NormalizeName(name);
    }

    public static string NormalizeName(string name)
    {
        string value = ListingRules.Text(name);
        // Whitespace/case normalization only; category synonym resolution is separate.
        string normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        Guard.Against(normalized.Length > 100, ListingErrors.InvalidCategory);
        return normalized;
    }
}

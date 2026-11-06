using SmartLost.BuildingBlocks.Core.Entities;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class Category : AggregateRoot<Guid>
{
    private Category()
    {
    }

    public string NormalizedName { get; private set; } = string.Empty;

    public static Category Create(string name)
    {
        return new Category { Id = Guid.NewGuid(), NormalizedName = NormalizeName(name) };
    }

    public void Rename(string name)
    {
        NormalizedName = NormalizeName(name);
    }

    private static string NormalizeName(string name)
    {
        string value = ListingRules.Text(name);
        // Whitespace/case normalization only; category synonym resolution is separate.
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }
}

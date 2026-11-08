using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Infrastructure.Persistence.Configurations;

/// <summary>Maps key sets to PostgreSQL text arrays; other providers (unit tests) store a delimited string.</summary>
public sealed class SearchListingConfiguration(bool usePostgreSqlArrays) : IEntityTypeConfiguration<SearchListing>
{
    private const char Separator = '\u001f';

    public void Configure(EntityTypeBuilder<SearchListing> builder)
    {
        builder.ToTable("SearchListings");
        builder.HasKey(listing => listing.Id);
        builder.Property(listing => listing.Id).ValueGeneratedNever();
        builder.Ignore(listing => listing.DomainEvents);
        builder.Ignore(listing => listing.CurrentPhoto);
        builder.Property(listing => listing.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(listing => listing.Location).HasMaxLength(SearchListing.MaximumLocationLength).IsRequired();
        builder.Property(listing => listing.Colour).HasMaxLength(SearchListing.MaximumColourLength);
        ConfigureKeys(builder.Property(listing => listing.CategoryKeys));
        ConfigureKeys(builder.Property(listing => listing.Traits));
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint("CK_SearchListings_CurrentPhoto",
            "(\"CurrentPhotoId\" IS NULL) = (\"CurrentPhotoRevision\" IS NULL)"));
        builder.HasIndex(listing => new { listing.Type, listing.Eligible });
    }

    private static readonly ValueComparer<IReadOnlySet<string>> _comparer = new(
        (left, right) => left!.SetEquals(right!),
        keys => keys.Order(StringComparer.Ordinal).Aggregate(0, (hash, key) => HashCode.Combine(hash, key)),
        keys => new HashSet<string>(keys, StringComparer.Ordinal));

    private void ConfigureKeys(PropertyBuilder<IReadOnlySet<string>> property)
    {
        if (usePostgreSqlArrays)
        {
            property.HasConversion(new ValueConverter<IReadOnlySet<string>, string[]>(
                keys => keys.Order(StringComparer.Ordinal).ToArray(),
                values => new HashSet<string>(values, StringComparer.Ordinal)), _comparer);
        }
        else
        {
            property.HasConversion(new ValueConverter<IReadOnlySet<string>, string>(
                keys => string.Join(Separator, keys.Order(StringComparer.Ordinal)),
                value => new HashSet<string>(value.Split(Separator, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)),
                _comparer);
        }

        property.IsRequired();
    }
}

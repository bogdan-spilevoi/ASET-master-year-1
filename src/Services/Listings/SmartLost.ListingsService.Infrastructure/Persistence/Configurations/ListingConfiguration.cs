using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence.Configurations;

public sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("Listings");
        builder.HasKey(listing => listing.Id);
        builder.Property(listing => listing.Id).ValueGeneratedNever();
        builder.Ignore(listing => listing.DomainEvents);
        builder.Property(listing => listing.Title).IsRequired();
        builder.Property(listing => listing.Description).IsRequired();
        builder.Property(listing => listing.Location).IsRequired();
        builder.Property(listing => listing.OccurredOn).HasColumnType("date");
        builder.Property(listing => listing.Revision).IsConcurrencyToken();
        builder.Property(listing => listing.ChangeSequence).IsConcurrencyToken();
        builder.PrimitiveCollection(listing => listing.Traits).HasField("_traits")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne(listing => listing.Photo).WithOne(photo => photo.Listing)
            .HasForeignKey<Photo>(photo => photo.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(listing => listing.Categories).WithOne(assignment => assignment.Listing)
            .HasForeignKey(assignment => assignment.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(listing => listing.Categories).HasField("_categories")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(listing => listing.AuthorId);
        builder.HasIndex(listing => new { listing.State, listing.Type });
    }
}

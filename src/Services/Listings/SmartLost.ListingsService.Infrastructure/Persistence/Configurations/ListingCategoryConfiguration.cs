using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence.Configurations;

public sealed class ListingCategoryConfiguration : IEntityTypeConfiguration<ListingCategory>
{
    public void Configure(EntityTypeBuilder<ListingCategory> builder)
    {
        builder.ToTable("ListingCategories");
        builder.HasKey(assignment => new { assignment.ListingId, assignment.CategoryId });
        builder.HasOne(assignment => assignment.Category).WithMany()
            .HasForeignKey(assignment => assignment.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

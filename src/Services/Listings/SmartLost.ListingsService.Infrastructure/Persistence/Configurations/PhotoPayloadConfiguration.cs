using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence.Configurations;

public sealed class PhotoPayloadConfiguration : IEntityTypeConfiguration<PhotoPayload>
{
    public void Configure(EntityTypeBuilder<PhotoPayload> builder)
    {
        builder.ToTable("PhotoPayloads");
        builder.HasKey(payload => payload.PhotoId);
        builder.HasOne<Photo>().WithOne().HasForeignKey<PhotoPayload>(payload => payload.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

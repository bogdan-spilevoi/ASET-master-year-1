using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence.Configurations;

public sealed class SecurityAuditRecordConfiguration : IEntityTypeConfiguration<SecurityAuditRecord>
{
    public void Configure(EntityTypeBuilder<SecurityAuditRecord> builder)
    {
        builder.ToTable("SecurityAuditRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.EventType).HasMaxLength(100).IsRequired();
        builder.Property(record => record.SubjectIdentifier).HasMaxLength(256);
        builder.Property(record => record.ClientIp).HasMaxLength(50);
        builder.Property(record => record.UserAgent).HasMaxLength(512);
        builder.Property(record => record.Succeeded).IsRequired();
        builder.Property(record => record.FailureReason).HasMaxLength(1024);
        builder.Property(record => record.OccurredAtUtc).IsRequired();

        builder.HasIndex(record => record.OccurredAtUtc);
        builder.HasIndex(record => record.EventType);
        builder.HasIndex(record => record.SubjectIdentifier);
    }
}

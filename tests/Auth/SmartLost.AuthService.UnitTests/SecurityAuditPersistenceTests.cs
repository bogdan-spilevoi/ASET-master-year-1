using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.Infrastructure.Security;
using SmartLost.BuildingBlocks.Application.Security;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class SecurityAuditPersistenceTests
{
    [Fact]
    public void SecurityAuditRecordCreateSetsPropertiesCorrectly()
    {
        var eventId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        var record = SecurityAuditRecord.Create(
            eventId,
            "UserLogin",
            "testuser",
            "127.0.0.1",
            "Browser/1.0",
            true,
            null,
            now);

        Assert.Equal(eventId, record.Id);
        Assert.Equal("UserLogin", record.EventType);
        Assert.Equal("testuser", record.SubjectIdentifier);
        Assert.Equal("127.0.0.1", record.ClientIp);
        Assert.Equal("Browser/1.0", record.UserAgent);
        Assert.True(record.Succeeded);
        Assert.Null(record.FailureReason);
        Assert.Equal(now, record.OccurredAtUtc);
    }

    [Fact]
    public void SecurityAuditRecordCreateWithNullEventTypeUsesDefault()
    {
        var record = SecurityAuditRecord.Create(
            Guid.Empty,
            "",
            null,
            null,
            null,
            false,
            "auth.invalid_credentials",
            DateTime.UtcNow);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal("Unknown", record.EventType);
        Assert.False(record.Succeeded);
        Assert.Equal("auth.invalid_credentials", record.FailureReason);
    }

    [Fact]
    public async Task DbSecurityAuditSinkPersistsRecordToDbContext()
    {
        string dbName = $"AuditTestDb_{Guid.NewGuid():N}";
        ServiceCollection services = new();
        services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(dbName));

        using ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        DbSecurityAuditSink sink = new(scopeFactory, NullLogger<DbSecurityAuditSink>.Instance);

        var eventId = Guid.NewGuid();
        DateTime timestamp = DateTime.UtcNow;
        SecurityAuditEvent auditEvent = new(
            eventId,
            "UserRegistration",
            "newuser@smartlost.local",
            "10.0.0.1",
            "Mozilla/5.0",
            true,
            null,
            timestamp);

        await sink.RecordAsync(auditEvent);

        using IServiceScope verifyScope = scopeFactory.CreateScope();
        AuthDbContext dbContext = verifyScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        SecurityAuditRecord? stored = await dbContext.SecurityAuditRecords.FirstOrDefaultAsync(r => r.Id == eventId);

        Assert.NotNull(stored);
        Assert.Equal("UserRegistration", stored.EventType);
        Assert.Equal("newuser@smartlost.local", stored.SubjectIdentifier);
        Assert.Equal("10.0.0.1", stored.ClientIp);
        Assert.Equal("Mozilla/5.0", stored.UserAgent);
        Assert.True(stored.Succeeded);
    }

    [Fact]
    public async Task DbSecurityAuditSinkThrowsArgumentNullExceptionForNullEvent()
    {
        ServiceCollection services = new();
        using ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        DbSecurityAuditSink sink = new(scopeFactory, NullLogger<DbSecurityAuditSink>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.RecordAsync(null!));
    }
}

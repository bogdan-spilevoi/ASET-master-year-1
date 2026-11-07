using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;

namespace SmartLost.AuthService.Infrastructure.Seeding;

/// <summary>Explicit development fixtures; existing records are preserved on sequential reruns.</summary>
public sealed class AuthDevelopmentSeeder(
    AuthDbContext context,
    IPasswordService passwordService)
{
    public const string DemoEmail = "demo@smartlost.local";
    public const string DemoUserName = "demouser";
    public const string DemoPassword = "DemoUser123!";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        string normalizedEmail = DemoEmail.Trim().ToUpperInvariant();
        bool exists = await context.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (!exists)
        {
            DateTime now = DateTime.UtcNow;
            var userAccount = UserAccount.Create(DemoUserName, DemoEmail, now);
            userAccount.SetPasswordHash(passwordService.Hash(userAccount, DemoPassword));
            context.Users.Add(userAccount);

            var auditRecord = SecurityAuditRecord.Create(
                Guid.NewGuid(),
                "UserRegistration",
                DemoUserName,
                "127.0.0.1",
                "SmartLost Seeder/1.0",
                true,
                null,
                now);
            context.SecurityAuditRecords.Add(auditRecord);

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}

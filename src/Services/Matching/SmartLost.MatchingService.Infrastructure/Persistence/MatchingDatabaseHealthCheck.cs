using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartLost.MatchingService.Infrastructure.Persistence;

/// <summary>
/// Read-only readiness check: the Matching database accepts connections and every checked-in
/// migration has been applied. It never creates, migrates or writes to the database.
/// </summary>
public sealed class MatchingDatabaseHealthCheck(MatchingDbContext dbContext) : IHealthCheck
{
    public const string Name = "matching-database";
    public const string ReadinessTag = "ready";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DatabaseFacade database = dbContext.Database;
        if (!await database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("The Matching database is unreachable.");
        }

        return FromPendingMigrations([.. await database.GetPendingMigrationsAsync(cancellationToken)]);
    }

    public static HealthCheckResult FromPendingMigrations(IReadOnlyCollection<string> pendingMigrations)
    {
        ArgumentNullException.ThrowIfNull(pendingMigrations);
        return pendingMigrations.Count == 0
            ? HealthCheckResult.Healthy("The Matching database schema is current.")
            : HealthCheckResult.Unhealthy($"The Matching database has {pendingMigrations.Count} pending migration(s).");
    }
}

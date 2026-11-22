using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using SmartLost.MatchingService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.MatchingService.IntegrationTests;

public sealed class MatchingReadinessTests
{
    [Fact]
    public async Task ReadinessFailsUntilMigratedAndNeverCreatesTheSchema()
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("matching_readiness_tests").WithUsername("matching_tests").WithPassword("test-only-database-password").Build();
        await database.StartAsync();
        DbContextOptions<MatchingDbContext> options = new DbContextOptionsBuilder<MatchingDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options;

        await using (var context = new MatchingDbContext(options))
        {
            HealthCheckResult empty = await new MatchingDatabaseHealthCheck(context).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, empty.Status);
            Assert.Equal("The Matching database has 1 pending migration(s).", empty.Description);
        }

        Assert.Equal(0L, await CountPublicTablesAsync(database.GetConnectionString()));

        await using (var context = new MatchingDbContext(options))
        {
            await context.Database.MigrateAsync();
            HealthCheckResult migrated = await new MatchingDatabaseHealthCheck(context).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, migrated.Status);
        }
    }

    private static async Task<long> CountPublicTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthMigrationTests
{
    [Fact]
    public void InitialMigrationMatchesTheCurrentPostgreSqlModel()
    {
        using AuthDbContext context = new AuthDbContextFactory().CreateDbContext([]);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.False(context.Database.HasPendingModelChanges());

        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        KeyValuePair<string, System.Reflection.TypeInfo> entry = assembly.Migrations.First();
        Assert.EndsWith("_InitialCreate", entry.Key, StringComparison.Ordinal);
        Migration migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        CreateTableOperation table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("Users", table.Name);
        Assert.Equal(7, table.Columns.Count);
        Assert.All(table.Columns, column => Assert.False(column.IsNullable));
        Assert.Contains(table.Columns, column => column.Name == "Id" && column.ColumnType == "uuid");
        Assert.Contains(table.Columns, column => column.Name == "CreatedAtUtc" && column.ColumnType == "timestamp with time zone");
        Assert.Contains(table.Columns, column => column.Name == "NormalizedUserName" && column.MaxLength == 100);
        Assert.Contains(table.Columns, column => column.Name == "NormalizedEmail" && column.MaxLength == 256);

        CreateIndexOperation[] indexes = [.. migration.UpOperations.OfType<CreateIndexOperation>()];
        Assert.Equal(2, indexes.Length);
        Assert.All(indexes, index => Assert.True(index.IsUnique));
        Assert.Contains(indexes, index => Assert.Single(index.Columns) == "NormalizedUserName");
        Assert.Contains(indexes, index => Assert.Single(index.Columns) == "NormalizedEmail");
        Assert.Equal("Users", Assert.Single(migration.DownOperations.OfType<DropTableOperation>()).Name);

        Microsoft.EntityFrameworkCore.Metadata.IEntityType entity = migration.TargetModel.FindEntityType(typeof(UserAccount).FullName!)!;
        Assert.Equal("Users", entity.GetTableName());
        Assert.Equal(2, entity.GetIndexes().Count());
    }

    [Fact]
    public void PostgreSqlScriptsIncludeHistoryAndUniqueIndexesWithoutAConnection()
    {
        using AuthDbContext context = new AuthDbContextFactory().CreateDbContext([]);
        IMigrator migrator = context.GetService<IMigrator>();
        string script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE \"Users\"", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Users_NormalizedUserName\"", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Users_NormalizedEmail\"", script, StringComparison.Ordinal);
        Assert.Contains("__EFMigrationsHistory", script, StringComparison.Ordinal);

        string id = context.GetService<IMigrationsAssembly>().Migrations.Last().Key;
        string rollback = migrator.GenerateScript(id, Migration.InitialDatabase);
        Assert.Contains("DROP TABLE \"Users\"", rollback, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM \"__EFMigrationsHistory\"", rollback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void StartupDoesNotApplyMigrations(string environment)
    {
        using AuthApiFactory factory = new();
        using WebApplicationFactory<Program> application = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                services.AddEntityFrameworkNpgsql();
                services.AddSingleton<IMigrator>(new RejectingMigrator());
                services.AddDbContext<AuthDbContext>((provider, options) => options
                    .UseNpgsql("Host=localhost;Database=unused")
                    .UseInternalServiceProvider(provider));
            });
        });

        using HttpClient client = application.CreateClient();
        Assert.Equal(environment, application.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        using IServiceScope scope = application.Services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.IsRelational());
    }

    private sealed class RejectingMigrator : IMigrator
    {
        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("API startup must not apply migrations.");
        }

        public void Migrate(string? targetMigration = null)
        {
            throw new InvalidOperationException("API startup must not apply migrations.");
        }

        public string GenerateScript(string? fromMigration = null, string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            throw new NotSupportedException();
        }

        public bool HasPendingModelChanges()
        {
            throw new NotSupportedException();
        }
    }
}

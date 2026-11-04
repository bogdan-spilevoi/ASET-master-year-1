using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.ListingsService.Infrastructure;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class ListingsPersistenceTests
{
    [Fact]
    public void InitialMigrationMatchesThePostgreSqlModelAndHasReversibleSevenTableSchema()
    {
        using ListingsDbContext context = new ListingsDbContextFactory().CreateDbContext([]);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.False(context.Database.HasPendingModelChanges());
        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        KeyValuePair<string, System.Reflection.TypeInfo> entry = Assert.Single(assembly.Migrations);
        Assert.EndsWith("_InitialCreate", entry.Key, StringComparison.Ordinal);
        Migration migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.Equal(7, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.Equal(7, migration.DownOperations.OfType<DropTableOperation>().Count());
        CreateTableOperation assignments = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name == "ListingCategories");
        Assert.Equal(["ListingId", "CategoryId"], assignments.PrimaryKey!.Columns);
        Assert.DoesNotContain(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name is "Users" or "OutboxMessages");
        Assert.Equal(7, migration.TargetModel.GetEntityTypes().Count());
        string script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("__EFMigrationsHistory", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Photos_ListingId\"", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Recoveries_MatchDecisionId\"", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_CredibilityEntries_RecoveryId\"", script, StringComparison.Ordinal);
        string rollback = context.GetService<IMigrator>().GenerateScript(entry.Key, Migration.InitialDatabase);
        Assert.Contains("DROP TABLE \"Listings\"", rollback, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignTimeSettingsAllowOfflineSchemaWorkAndExplicitConnectionOverrides()
    {
        string directory = Path.Combine(Path.GetTempPath(), "listings-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "appsettings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                ConnectionStrings = new
                {
                    ListingsDatabase = "Host=json-settings;Database=unused"
                }
            }));
            using ListingsDbContext fromFile = new ListingsDbContextFactory().CreateDbContext(["--SettingsFile", path]);
            Assert.Equal("Host=json-settings;Database=unused", fromFile.Database.GetConnectionString());
            string environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            File.WriteAllText(Path.Combine(directory, $"appsettings.{environment}.json"),
                "{\"ConnectionStrings\":{\"ListingsDatabase\":\"Host=environment-settings;Database=unused\"}}");
            using ListingsDbContext fromEnvironmentFile = new ListingsDbContextFactory().CreateDbContext(["--SettingsFile", path]);
            Assert.Equal("Host=environment-settings;Database=unused", fromEnvironmentFile.Database.GetConnectionString());
            using ListingsDbContext overridden = new ListingsDbContextFactory().CreateDbContext(
                ["--SettingsFile", path, "--ConnectionStrings:ListingsDatabase", "Host=explicit;Database=unused"]);
            Assert.Equal("Host=explicit;Database=unused", overridden.Database.GetConnectionString());
            File.Delete(Path.Combine(directory, $"appsettings.{environment}.json"));
            File.WriteAllText(path, "{}");
            using ListingsDbContext offline = new ListingsDbContextFactory().CreateDbContext(["--SettingsFile", path]);
            Assert.Equal("Host=localhost;Port=5433;Database=listingsservice;Username=listingsservice", offline.Database.GetConnectionString());
            Assert.Throws<FileNotFoundException>(() => new ListingsDbContextFactory().CreateDbContext(["--SettingsFile", Path.Combine(directory, "missing.json")]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RegistrationIsScopedRequiresConfigurationAndDoesNotConnectOrMigrate()
    {
        IConfiguration missing = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddListingsPersistence(missing));
        IConfiguration blank = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ListingsDatabase"] = " "
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddListingsPersistence(blank));
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ListingsDatabase"] = "Host=unused;Database=listingsservice;Username=unused"
        }).Build();
        var services = new ServiceCollection();
        Assert.Same(services, services.AddListingsPersistence(configuration));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        ListingsDbContext context = first.ServiceProvider.GetRequiredService<ListingsDbContext>();
        Assert.Same(context, first.ServiceProvider.GetRequiredService<ListingsDbContext>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<ListingsDbContext>());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal("Listings", context.Listings.EntityType.GetTableName());
        Assert.Equal("Photos", context.Photos.EntityType.GetTableName());
        Assert.Equal("Categories", context.Categories.EntityType.GetTableName());
        Assert.Equal("ListingCategories", context.ListingCategories.EntityType.GetTableName());
        Assert.Equal("MatchDecisions", context.MatchDecisions.EntityType.GetTableName());
        Assert.Equal("Recoveries", context.Recoveries.EntityType.GetTableName());
        Assert.Equal("CredibilityEntries", context.CredibilityEntries.EntityType.GetTableName());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}

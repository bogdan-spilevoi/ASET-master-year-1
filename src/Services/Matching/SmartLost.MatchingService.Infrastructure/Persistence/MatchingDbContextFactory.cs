using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmartLost.MatchingService.Infrastructure.Persistence;

public sealed class MatchingDbContextFactory : IDesignTimeDbContextFactory<MatchingDbContext>
{
    public const string ConnectionStringName = "MatchingDatabase";

    public MatchingDbContext CreateDbContext(string[] args)
    {
        // Schema-only commands need the provider/model, not a running API or database.
        IConfigurationRoot arguments = new ConfigurationBuilder().AddCommandLine(args).Build();
        string path = arguments["SettingsFile"] ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(path, optional: arguments["SettingsFile"] is null)
            .AddJsonFile(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $"appsettings.{environment}.json"), optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? "Host=localhost;Port=5434;Database=matchingservice;Username=matchingservice";
        var options = new DbContextOptionsBuilder<MatchingDbContext>();
        options.UseNpgsql(connectionString);
        return new MatchingDbContext(options.Options);
    }
}

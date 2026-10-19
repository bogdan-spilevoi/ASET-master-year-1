using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
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
        string connectionString = configuration.GetConnectionString("AuthDatabase")
            ?? "Host=localhost;Port=5432;Database=authservice;Username=authservice";
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        options.UseNpgsql(connectionString);
        return new AuthDbContext(options.Options);
    }
}

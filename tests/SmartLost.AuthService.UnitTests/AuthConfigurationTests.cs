using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartLost.AuthService.Infrastructure;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.AuthService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public AuthConfigurationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void EfToolingReadsJsonAndCommandLineOverridesIt()
    {
        string path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            ConnectionStrings = new
            {
                AuthDatabase = "Host=json-settings;Database=unused"
            }
        }));
        using AuthDbContext context = new AuthDbContextFactory().CreateDbContext(["--SettingsFile", path]);
        Assert.Equal("Host=json-settings;Database=unused", context.Database.GetConnectionString());

        using AuthDbContext overridden = new AuthDbContextFactory().CreateDbContext(
            ["--SettingsFile", path, "--ConnectionStrings:AuthDatabase", "Host=explicit;Database=unused"]);
        Assert.Equal("Host=explicit;Database=unused", overridden.Database.GetConnectionString());
    }

    [Fact]
    public void EfToolingReadsEnvironmentSpecificJson()
    {
        string path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, "{}");
        string environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        File.WriteAllText(Path.Combine(_directory, $"appsettings.{environment}.json"), JsonSerializer.Serialize(new
        {
            ConnectionStrings = new
            {
                AuthDatabase = "Host=environment-settings;Database=unused"
            }
        }));

        using AuthDbContext context = new AuthDbContextFactory().CreateDbContext(["--SettingsFile", path]);
        Assert.Equal("Host=environment-settings;Database=unused", context.Database.GetConnectionString());
    }

    [Fact]
    public void EfToolingAllowsSchemaInspectionWithoutLocalCredentials()
    {
        string path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, "{}");
        using AuthDbContext context = new AuthDbContextFactory().CreateDbContext(["--SettingsFile", path]);
        Assert.Equal("Host=localhost;Port=5432;Database=authservice;Username=authservice", context.Database.GetConnectionString());
    }

    [Fact]
    public void ExplicitMissingSettingsFileFailsInsteadOfUsingAnotherDatabase()
    {
        string path = Path.Combine(_directory, "missing.json");
        Assert.Throws<FileNotFoundException>(() => new AuthDbContextFactory().CreateDbContext(["--SettingsFile", path]));
    }

    [Theory]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", " ")]
    [InlineData("Jwt:SigningKey", "short-key")]
    [InlineData("Jwt:SigningKey", "")]
    [InlineData("Jwt:ExpiryMinutes", "0")]
    [InlineData("Jwt:RefreshTokenExpiryDays", "0")]
    [InlineData("Jwt:RefreshTokenExpiryDays", "91")]
    public async Task InvalidJwtConfigurationFailsAtStartup(string key, string value)
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings[key] = value;
        using IHost host = CreateHost(settings);

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal(typeof(JwtOptions), exception.OptionsType);
        Assert.NotEmpty(exception.Failures);
    }

    [Fact]
    public async Task ValidConfigurationStartsAndConfiguresTheDatabaseWithoutConnecting()
    {
        using IHost host = CreateHost(ValidSettings());
        await host.StartAsync();
        using IServiceScope scope = host.Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal("Host=local-test;Database=unused", context.Database.GetConnectionString());
        Assert.Equal("AuthServiceTests", host.Services.GetRequiredService<IOptions<JwtOptions>>().Value.Issuer);
        await host.StopAsync();
    }

    [Fact]
    public void MissingDatabaseConfigurationFailsWithAnActionableMessage()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        IServiceCollection services = new ServiceCollection();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure(configuration));
        Assert.Contains("ConnectionStrings:AuthDatabase", exception.Message, StringComparison.Ordinal);
    }

    private static IHost CreateHost(Dictionary<string, string?> settings)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) => services.AddInfrastructure(context.Configuration))
            .Build();
    }

    private static Dictionary<string, string?> ValidSettings()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:AuthDatabase"] = "Host=local-test;Database=unused",
            ["Jwt:Issuer"] = "AuthServiceTests",
            ["Jwt:Audience"] = "AuthServiceTests",
            ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-characters",
            ["Jwt:ExpiryMinutes"] = "60"
        };
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}

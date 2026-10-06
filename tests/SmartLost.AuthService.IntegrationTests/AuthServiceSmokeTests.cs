using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.AuthService.IntegrationTests;

public sealed class AuthServiceSmokeTests : IClassFixture<AuthServiceFactory>
{
    private readonly HttpClient _client;

    public AuthServiceSmokeTests(AuthServiceFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RegisterThenLoginWorksWithFreshDatabase()
    {
        RegisterRequest request = new()
        {
            UserName = "  Maria.Ionescu  ",
            Email = "  Maria@Example.com  ",
            Password = "P@ssw0rd123!"
        };
        HttpResponseMessage register = await _client.PostAsJsonAsync("/api/auth/register", request);
        HttpResponseMessage login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "  MARIA@EXAMPLE.COM  ", Password = request.Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AuthResponse? registered = await register.Content.ReadFromJsonAsync<AuthResponse>();
        AuthResponse? authenticated = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registered);
        Assert.NotNull(authenticated);
        Assert.Equal(registered.UserId, authenticated.UserId);
        Assert.Equal("Maria.Ionescu", registered.UserName);
        Assert.Equal("Maria@Example.com", registered.Email);
        Assert.Equal(registered.UserName, authenticated.UserName);
        Assert.Equal(registered.Email, authenticated.Email);
        Assert.NotEmpty(authenticated.AccessToken);

        HttpResponseMessage duplicate = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "maria.ionescu",
            Email = "maria@example.com",
            Password = request.Password
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }
}

public sealed class AuthServiceFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AuthDatabase"] = "Host=localhost;Database=test-only",
            ["Jwt:Issuer"] = "AuthServiceTests",
            ["Jwt:Audience"] = "AuthServiceTests",
            ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-characters",
            ["Jwt:ExpiryMinutes"] = "60"
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase("AuthServiceIntegrationTests"));
        });
    }
}

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
using SmartLost.AuthService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthApiTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;

    public AuthApiTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuthenticationEndpointsReturnExpectedResponses()
    {
        RegisterRequest request = new()
        {
            UserName = "maria.ionescu",
            Email = "maria.ionescu@example.com",
            Password = "P@ssw0rd123!"
        };

        HttpResponseMessage registerResponse = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        HttpResponseMessage duplicateResponse = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        HttpResponseMessage invalidLoginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { UserNameOrEmail = string.Empty, Password = string.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLoginResponse.StatusCode);

        HttpResponseMessage invalidPasswordResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { UserNameOrEmail = request.UserName, Password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidPasswordResponse.StatusCode);

        HttpResponseMessage loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { UserNameOrEmail = request.Email, Password = request.Password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }
}

public sealed class AuthApiFactory : WebApplicationFactory<Program>
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
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase("AuthServiceApiTests"));
        });
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Core.Results;
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
        AuthResponse? registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registered);
        Assert.Equal(request.UserName, registered.UserName);
        Assert.NotEmpty(registered.AccessToken);

        HttpResponseMessage duplicateResponse = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("auth.account_exists", (await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["code"]!.ToString());

        HttpResponseMessage invalidLoginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = string.Empty, Password = string.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLoginResponse.StatusCode);

        HttpResponseMessage invalidPasswordResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = request.Email, Password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidPasswordResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidPasswordResponse.Content.Headers.ContentType!.MediaType);

        HttpResponseMessage loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = request.Email, Password = request.Password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(registered.UserId, (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>())!.UserId);
    }

    [Fact]
    public async Task ApplicationPipelineRejectsInvalidCommandsBeforePersistence()
    {
        using AuthApiFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        Result<AuthResponse> invalid = await sender.Send(new RegisterUserCommand("  x  ", "invalid-email", "short"));
        Assert.True(invalid.IsFailure);
        Assert.Equal(3, invalid.Errors.Count);
        Assert.All(invalid.Errors, error => Assert.Equal(ErrorKind.Validation, error.Kind));
        Assert.Contains(invalid.Errors, error => error.PropertyName == "UserName");
        AuthDbContext database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.False(await database.Users.AnyAsync(user => user.UserName == "x"));
    }

    [Theory]
    [InlineData("  x  ", "valid@example.com", "ValidPassword123!")]
    [InlineData("valid-user", "invalid-email", "ValidPassword123!")]
    [InlineData("valid-user", "valid@example.com", "short")]
    public async Task InvalidRegistrationReturnsFieldErrors(string userName, string email, string password)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { UserName = userName, Email = email, Password = password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        JsonElement errors = Assert.IsType<JsonElement>(problem.Extensions["errors"]);
        Assert.NotEmpty(errors.EnumerateArray());
        Assert.True(errors[0].TryGetProperty("propertyName", out _));
    }

    [Fact]
    public async Task UnknownUserReturnsUnauthorized()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "unknown@example.com", Password = "password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginRejectsUserNameAndTheOldRequestField()
    {
        HttpResponseMessage userNameResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "maria.ionescu", Password = "P@ssw0rd123!" });
        Assert.Equal(HttpStatusCode.BadRequest, userNameResponse.StatusCode);

        HttpResponseMessage oldRequestResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new
            {
                userNameOrEmail = "maria.ionescu@example.com",
                password = "P@ssw0rd123!"
            });
        Assert.Equal(HttpStatusCode.BadRequest, oldRequestResponse.StatusCode);
    }

    [Fact]
    public async Task EmailLookupDoesNotMatchAnotherAccountsUserName()
    {
        using AuthApiFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        IUserAccountRepository repository = scope.ServiceProvider.GetRequiredService<IUserAccountRepository>();
        var userNameMatch = UserAccount.Create("lookup@example.com", "different@example.com", DateTime.UtcNow);
        var emailMatch = UserAccount.Create("lookup-user", "lookup@example.com", DateTime.UtcNow);
        repository.Add(userNameMatch);
        repository.Add(emailMatch);
        await repository.SaveChangesAsync(CancellationToken.None);

        UserAccount? found = await repository.FindByEmailAsync("LOOKUP@EXAMPLE.COM", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(emailMatch.Id, found.Id);
    }

    [Fact]
    public async Task UnexpectedExceptionsReturn500WithoutExposingTheirMessage()
    {
        using AuthApiFactory factory = new();
        using WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserAccountRepository>();
                services.AddScoped<IUserAccountRepository, FailingRepository>();
            }));
        using HttpClient client = failingFactory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { UserName = "valid-user", Email = "valid@example.com", Password = "ValidPassword123!" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal-database-detail", body, StringComparison.Ordinal);
        Assert.Contains("error.unexpected", body, StringComparison.Ordinal);
    }

    private sealed class FailingRepository : IUserAccountRepository
    {
        public Task<bool> ExistsAsync(string normalizedUserName, string normalizedEmail, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("internal-database-detail");
        }

        public Task<UserAccount?> FindByEmailAsync(string normalizedValue, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void Add(UserAccount userAccount)
        {
            throw new NotSupportedException();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
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

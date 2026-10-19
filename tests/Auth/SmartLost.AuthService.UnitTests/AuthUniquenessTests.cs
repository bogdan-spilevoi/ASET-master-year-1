using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthUniquenessTests
{
    [Fact]
    public async Task RegistrationRejectsDuplicateUserNameWithADifferentEmail()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage first = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { UserName = "Unique.User", Email = "first@example.com", Password = "Password123!" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        HttpResponseMessage duplicate = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { UserName = "  UNIQUE.USER  ", Email = "second@example.com", Password = "Password123!" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var body = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("auth.account_exists", body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("23505", "IX_Users_NormalizedUserName", HttpStatusCode.Conflict, "UserName")]
    [InlineData("23505", "IX_Users_NormalizedEmail", HttpStatusCode.Conflict, "Email")]
    [InlineData("23505", "PK_Users", HttpStatusCode.InternalServerError, null)]
    [InlineData("23503", "IX_Users_NormalizedUserName", HttpStatusCode.InternalServerError, null)]
    public async Task DatabaseIdentityConflictsAreMappedThroughThePipeline(
        string sqlState, string constraint, HttpStatusCode expectedStatus, string? propertyName)
    {
        using AuthApiFactory factory = new();
        var databaseError = new PostgresException("private-database-detail", "ERROR", "ERROR", sqlState, constraintName: constraint);
        using WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddDbContext<AuthDbContext>(options =>
                options.AddInterceptors(new FailingSaveInterceptor(new DbUpdateException("private-save-detail", databaseError))))));
        using HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { UserName = "race-user", Email = "race@example.com", Password = "Password123!" });

        Assert.Equal(expectedStatus, response.StatusCode);
        string text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-database-detail", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-save-detail", text, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(text);
        if (propertyName is not null)
        {
            Assert.Equal("auth.account_exists", body.RootElement.GetProperty("code").GetString());
            Assert.Equal(propertyName, body.RootElement.GetProperty("errors")[0].GetProperty("propertyName").GetString());
        }
        else
        {
            Assert.Equal("error.unexpected", body.RootElement.GetProperty("code").GetString());
        }
    }

    private sealed class FailingSaveInterceptor(DbUpdateException exception) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }
}

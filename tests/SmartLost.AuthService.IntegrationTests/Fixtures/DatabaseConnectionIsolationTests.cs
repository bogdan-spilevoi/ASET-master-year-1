using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.IntegrationTests.Helpers;
using Xunit;

namespace SmartLost.AuthService.IntegrationTests.Fixtures;

public sealed class DatabaseConnectionIsolationTests(AuthServiceFactory factory) : IClassFixture<AuthServiceFactory>
{
    [Fact]
    public async Task ScopesUseIndependentConnectionsToTheSameDatabase()
    {
        UserAccount user = await new TestDataSeeder(factory).SeedUserAsync();
        await using AsyncServiceScope firstScope = factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = factory.Services.CreateAsyncScope();
        AuthDbContext first = firstScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        AuthDbContext second = secondScope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await first.Database.OpenConnectionAsync();
        await using DbCommand command = first.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        int firstBackendId = reader.GetInt32(0);

        Assert.NotSame(first, second);
        Assert.NotSame(first.Database.GetDbConnection(), second.Database.GetDbConnection());
        Assert.Equal(first.Database.GetDbConnection().Database, second.Database.GetDbConnection().Database);
        // Query through the second scope while the first connection still has an active reader.
        await second.Database.OpenConnectionAsync();
        Assert.True(await second.Users.AnyAsync(item => item.Id == user.Id));
        await using DbCommand secondCommand = second.Database.GetDbConnection().CreateCommand();
        secondCommand.CommandText = "SELECT pg_backend_pid()";
        int secondBackendId = (int)(await secondCommand.ExecuteScalarAsync())!;
        Assert.NotEqual(firstBackendId, secondBackendId);
    }

    [Fact]
    public async Task AuthenticatedClientUsesTheSeededIdentityAndRealPasswordHash()
    {
        (UserAccount user, HttpClient client) = await new TestDataSeeder(factory).CreateAuthenticatedClientAsync();
        using (client)
        {
            Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
            JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization.Parameter);
            Assert.Equal(user.Id.ToString(), token.Subject);
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        UserAccount persisted = await context.Users.SingleAsync(item => item.Id == user.Id);
        Assert.NotEqual(TestRequestFactory.DefaultPassword, persisted.PasswordHash);
        Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordService>()
            .Verify(persisted, TestRequestFactory.DefaultPassword));
    }
}

using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.IntegrationTests.Fixtures;

namespace SmartLost.AuthService.IntegrationTests.Helpers;

public sealed class TestDataSeeder(AuthServiceFactory factory)
{
    public async Task<UserAccount> SeedUserAsync(
        string? email = null,
        string? userName = null,
        string password = TestRequestFactory.DefaultPassword)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        IPasswordService passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        RegisterRequest request = TestRequestFactory.ValidRegisterRequest(email, userName);
        var user = UserAccount.Create(request.UserName, request.Email, DateTime.UtcNow);
        user.SetPasswordHash(passwordService.Hash(user, password));
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public async Task<(UserAccount User, HttpClient Client)> CreateAuthenticatedClientAsync()
    {
        UserAccount user = await SeedUserAsync();
        return (user, factory.CreateAuthenticatedClient(user));
    }
}

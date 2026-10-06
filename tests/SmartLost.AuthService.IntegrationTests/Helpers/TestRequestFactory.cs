using SmartLost.AuthService.Api.Contracts;

namespace SmartLost.AuthService.IntegrationTests.Helpers;

public static class TestRequestFactory
{
    public const string DefaultPassword = "Password123!";

    public static RegisterRequest ValidRegisterRequest(string? email = null, string? userName = null)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        return new RegisterRequest
        {
            Email = email ?? $"user-{suffix}@example.com",
            UserName = userName ?? $"user-{suffix}",
            Password = DefaultPassword
        };
    }

    public static LoginRequest ValidLoginRequest(string email, string password = DefaultPassword)
    {
        return new LoginRequest { Email = email, Password = password };
    }

    public static RefreshRequest ValidRefreshRequest(string refreshToken)
    {
        return new RefreshRequest { RefreshToken = refreshToken };
    }
}

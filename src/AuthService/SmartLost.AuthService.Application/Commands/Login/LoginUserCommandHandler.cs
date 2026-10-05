using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed class LoginUserCommandHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordService passwordService,
    ITokenService tokenService) : IRequestHandler<LoginUserCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        Validate(request);

        UserAccount? userAccount = await userAccountRepository.FindByUserNameOrEmailAsync(
            Normalize(request.UserNameOrEmail),
            cancellationToken);

        if (userAccount is null || !passwordService.Verify(userAccount, request.Password))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        TokenResult token = tokenService.CreateToken(userAccount);
        return new AuthResponse(userAccount.Id, userAccount.UserName, userAccount.Email, token.AccessToken, token.ExpiresAtUtc);
    }

    private static void Validate(LoginUserCommand request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserNameOrEmail))
        {
            throw new ArgumentException("User name or email is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Password is required.", nameof(request));
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}

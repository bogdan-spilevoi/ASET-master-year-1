using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Application.Commands.Register;

public sealed class RegisterUserCommandHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordService passwordService,
    ITokenService tokenService) : IRequestHandler<RegisterUserCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        Validate(request);

        string userName = request.UserName.Trim();
        string email = request.Email.Trim();
        string normalizedUserName = Normalize(userName);
        string normalizedEmail = Normalize(email);
        bool userExists = await userAccountRepository.ExistsAsync(normalizedUserName, normalizedEmail, cancellationToken);

        if (userExists)
        {
            throw new InvalidOperationException("User name or email already exists.");
        }

        var userAccount = UserAccount.Create(userName, normalizedUserName, email, normalizedEmail, DateTime.UtcNow);
        userAccount.SetPasswordHash(passwordService.Hash(userAccount, request.Password));
        userAccountRepository.Add(userAccount);
        await userAccountRepository.SaveChangesAsync(cancellationToken);

        return CreateResponse(userAccount);
    }

    private AuthResponse CreateResponse(UserAccount userAccount)
    {
        TokenResult token = tokenService.CreateToken(userAccount);
        return new AuthResponse(userAccount.Id, userAccount.UserName, userAccount.Email, token.AccessToken, token.ExpiresAtUtc);
    }

    private static void Validate(RegisterUserCommand request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            throw new ArgumentException("User name is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ArgumentException("Email is required.", nameof(request));
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

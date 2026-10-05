using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Identity;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed class LoginUserCommandHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordService passwordService,
    ITokenService tokenService) : IRequestHandler<LoginUserCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        UserAccount? userAccount = await userAccountRepository.FindByUserNameOrEmailAsync(
            UserIdentityNormalizer.Normalize(request.UserNameOrEmail),
            cancellationToken);

        if (userAccount is null || !passwordService.Verify(userAccount, request.Password))
        {
            return Result<AuthResponse>.Failure(new Error("auth.invalid_credentials", "Invalid credentials.", ErrorKind.Unauthorized));
        }

        TokenResult token = tokenService.CreateToken(userAccount);
        return Result<AuthResponse>.Success(new AuthResponse(userAccount.Id, userAccount.UserName, userAccount.Email, token.AccessToken, token.ExpiresAtUtc));
    }
}

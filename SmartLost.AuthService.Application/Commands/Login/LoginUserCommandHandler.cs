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
    IAuthenticationSessionService sessionService) : IRequestHandler<LoginUserCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        UserAccount? userAccount = await userAccountRepository.FindByEmailAsync(
            UserIdentityNormalizer.Normalize(request.Email),
            cancellationToken);

        if (userAccount is null || !passwordService.Verify(userAccount, request.Password))
        {
            return Result<AuthResponse>.Failure(new Error("auth.invalid_credentials", "Invalid credentials.", ErrorKind.Unauthorized));
        }

        AuthResponse response = await sessionService.CreateAsync(userAccount, cancellationToken);
        return Result<AuthResponse>.Success(response);
    }
}

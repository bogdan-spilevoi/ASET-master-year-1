using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Errors;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Register;

public sealed class RegisterUserCommandHandler(
    IUserAccountRepository userAccountRepository,
    IPasswordService passwordService,
    IAuthenticationSessionService sessionService) : IRequestHandler<RegisterUserCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var userAccount = UserAccount.Create(request.UserName, request.Email, DateTime.UtcNow);
        bool userExists = await userAccountRepository.ExistsAsync(
            userAccount.NormalizedUserName, userAccount.NormalizedEmail, cancellationToken);

        if (userExists)
        {
            return Result<AuthResponse>.Failure(AuthErrors.AccountExists);
        }

        userAccount.SetPasswordHash(passwordService.Hash(userAccount, request.Password));
        userAccountRepository.Add(userAccount);
        AuthResponse response = await sessionService.CreateAsync(userAccount, cancellationToken);
        return Result<AuthResponse>.Success(response);
    }
}

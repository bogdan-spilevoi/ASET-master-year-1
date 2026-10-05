using FluentValidation;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(command => command.UserNameOrEmail).NotEmpty().WithErrorCode("auth.identity_required");
        RuleFor(command => command.Password).NotEmpty().WithErrorCode("auth.password_required");
    }
}

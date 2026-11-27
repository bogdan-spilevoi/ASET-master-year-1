using FluentValidation;
using SmartLost.AuthService.Application.Validation;

namespace SmartLost.AuthService.Application.Commands.Logout;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RefreshTokenRules.ApplyTo(RuleFor(command => command.RefreshToken));
    }
}

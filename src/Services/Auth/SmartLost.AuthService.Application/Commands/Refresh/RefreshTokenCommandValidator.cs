using FluentValidation;
using SmartLost.AuthService.Application.Validation;

namespace SmartLost.AuthService.Application.Commands.Refresh;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RefreshTokenRules.ApplyTo(RuleFor(command => command.RefreshToken));
    }
}

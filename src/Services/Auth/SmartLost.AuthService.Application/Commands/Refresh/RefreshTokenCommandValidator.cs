using FluentValidation;

namespace SmartLost.AuthService.Application.Commands.Refresh;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.refresh_token_required")
            .Matches("\\A[A-Za-z0-9_-]{86}\\z").WithErrorCode("auth.invalid_refresh_token");
    }
}

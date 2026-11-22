using FluentValidation;

namespace SmartLost.AuthService.Application.Commands.ConfirmEmail;

public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(command => command.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.confirmation_token_required")
            .Matches("\\A[A-Za-z0-9_-]{43,86}\\z").WithErrorCode("auth.invalid_confirmation_token");
    }
}

using FluentValidation;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.email_required")
            .Must(value => value.Trim().Length <= 256)
            .WithMessage("Email must contain at most 256 characters after trimming.")
            .WithErrorCode("auth.invalid_email")
            .EmailAddress().WithErrorCode("auth.invalid_email");
        RuleFor(command => command.Password).NotEmpty().WithErrorCode("auth.password_required");
    }
}

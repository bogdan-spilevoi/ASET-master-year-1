using FluentValidation;

namespace SmartLost.AuthService.Application.Commands.Register;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(command => command.UserName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.username_required")
            .Must(value => value.Trim().Length is >= 3 and <= 100)
            .WithMessage("User name must contain between 3 and 100 characters after trimming.")
            .WithErrorCode("auth.invalid_username");
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.email_required")
            .Must(value => value.Trim().Length <= 256)
            .WithMessage("Email must contain at most 256 characters after trimming.")
            .WithErrorCode("auth.invalid_email")
            .EmailAddress().WithErrorCode("auth.invalid_email");
        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.password_required")
            .MinimumLength(8).WithErrorCode("auth.invalid_password");
    }
}

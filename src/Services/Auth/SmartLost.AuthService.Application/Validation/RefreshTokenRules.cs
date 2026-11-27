using FluentValidation;

namespace SmartLost.AuthService.Application.Validation;

internal static class RefreshTokenRules
{
    internal static void ApplyTo<T>(IRuleBuilderInitial<T, string> rule)
    {
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("auth.refresh_token_required")
            .Matches(@"\A[A-Za-z0-9_-]{86}\z").WithErrorCode("auth.invalid_refresh_token");
    }
}

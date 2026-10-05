using System.ComponentModel.DataAnnotations;

namespace SmartLost.AuthService.Api.Contracts;

public sealed class LoginRequest
{
    [Required]
    public string UserNameOrEmail { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

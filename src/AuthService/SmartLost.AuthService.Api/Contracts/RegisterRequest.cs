using System.ComponentModel.DataAnnotations;

namespace SmartLost.AuthService.Api.Contracts;

public sealed class RegisterRequest
{
    [Required]
    [MinLength(3)]
    public string UserName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;
}

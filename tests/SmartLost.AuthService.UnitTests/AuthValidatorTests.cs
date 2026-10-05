using FluentValidation.Results;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthValidatorTests
{
    [Fact]
    public async Task RegistrationChecksTrimmedLengthsAndRequiredFields()
    {
        RegisterUserCommandValidator validator = new();
        Assert.True((await validator.ValidateAsync(new RegisterUserCommand(" abc ", " abc@example.com ", "password"))).IsValid);
        Assert.True((await validator.ValidateAsync(new RegisterUserCommand(new string('a', 100), "abc@example.com", "password"))).IsValid);
        Assert.False((await validator.ValidateAsync(new RegisterUserCommand(new string('a', 101), "abc@example.com", "password"))).IsValid);
        Assert.False((await validator.ValidateAsync(new RegisterUserCommand("valid", new string('a', 250) + "@example.com", "password"))).IsValid);
        Assert.Equal(3, (await validator.ValidateAsync(new RegisterUserCommand(" ", " ", " "))).Errors.Count);
        Assert.Equal(3, (await validator.ValidateAsync(new RegisterUserCommand(null!, null!, null!))).Errors.Count);
    }

    [Fact]
    public async Task LoginRejectsMissingEmailAndPassword()
    {
        LoginUserCommandValidator validator = new();
        Assert.True((await validator.ValidateAsync(new LoginUserCommand(" user@example.com ", "password"))).IsValid);
        Assert.Equal(2, (await validator.ValidateAsync(new LoginUserCommand(" ", " "))).Errors.Count);
        Assert.Equal(2, (await validator.ValidateAsync(new LoginUserCommand(null!, null!))).Errors.Count);
    }

    [Theory]
    [InlineData("user-name")]
    [InlineData("invalid-email")]
    public async Task LoginRejectsInvalidEmail(string email)
    {
        LoginUserCommandValidator validator = new();
        ValidationResult result = await validator.ValidateAsync(new LoginUserCommand(email, "password"));
        Assert.Equal("auth.invalid_email", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public async Task LoginRejectsEmailLongerThanTheDatabaseLimit()
    {
        LoginUserCommandValidator validator = new();
        ValidationResult result = await validator.ValidateAsync(new LoginUserCommand(new string('a', 250) + "@example.com", "password"));
        Assert.Equal("auth.invalid_email", Assert.Single(result.Errors).ErrorCode);
    }
}

using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;

namespace SmartLost.AuthService.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            AuthResponse response = await sender.Send(new RegisterUserCommand(request.UserName, request.Email, request.Password), cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            AuthResponse response = await sender.Send(new LoginUserCommand(request.UserNameOrEmail, request.Password), cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new
            {
                error = ex.Message
            });
        }
    }
}

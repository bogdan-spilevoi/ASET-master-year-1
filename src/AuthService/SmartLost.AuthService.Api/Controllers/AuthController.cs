using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender, IMapper mapper) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        RegisterUserCommand command = mapper.Map<RegisterUserCommand>(request);
        Result<AuthResponse> result = await sender.Send(command, cancellationToken);
        return this.ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        LoginUserCommand command = mapper.Map<LoginUserCommand>(request);
        Result<AuthResponse> result = await sender.Send(command, cancellationToken);
        return this.ToActionResult(result);
    }
}

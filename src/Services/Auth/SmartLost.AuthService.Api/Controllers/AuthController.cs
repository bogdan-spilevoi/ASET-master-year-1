using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Refresh;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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

    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        RefreshTokenCommand command = mapper.Map<RefreshTokenCommand>(request);
        Result<AuthResponse> result = await sender.Send(command, cancellationToken);
        return this.ToActionResult(result);
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

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public IActionResult Me([FromServices] ICurrentUser currentUser)
    {
        return Ok(new CurrentUserResponse(
            currentUser.UserId,
            currentUser.UserName,
            currentUser.Email,
            currentUser.IsAuthenticated));
    }

    [HttpGet("security/audit-logs")]
    [ProducesResponseType<IReadOnlyList<SecurityAuditLogResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogs(
        [FromServices] AuthDbContext dbContext,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        int bounded = Math.Clamp(limit, 1, 100);
        List<SecurityAuditLogResponse> logs = await dbContext.SecurityAuditRecords
            .AsNoTracking()
            .OrderByDescending(record => record.OccurredAtUtc)
            .Take(bounded)
            .Select(record => new SecurityAuditLogResponse(
                record.Id,
                record.EventType,
                record.SubjectIdentifier,
                record.ClientIp,
                record.UserAgent,
                record.Succeeded,
                record.FailureReason,
                record.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }
}

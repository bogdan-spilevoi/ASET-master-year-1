using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Api.Pages;
using SmartLost.AuthService.Application.Commands.ConfirmEmail;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Logout;
using SmartLost.AuthService.Application.Commands.Refresh;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Queries.GetCurrentUser;
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

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        LogoutCommand command = mapper.Map<LogoutCommand>(request);
        return this.ToActionResult(await sender.Send(command, cancellationToken));
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

    [HttpGet("confirm-email")]
    [Produces("text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest, "text/html")]
    public IActionResult ConfirmEmailPage([FromQuery] string? token = null)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.XFrameOptions = "DENY";
        Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'";

        if (string.IsNullOrWhiteSpace(token))
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentType = "text/html; charset=utf-8",
                Content = EmailConfirmationPageRenderer.RenderMissingTokenPage()
            };
        }

        return Content(EmailConfirmationPageRenderer.RenderConfirmationPage(token), "text/html; charset=utf-8");
    }

    [HttpPost("confirm-email")]
    [ProducesResponseType<EmailConfirmationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        ConfirmEmailCommand command = mapper.Map<ConfirmEmailCommand>(request);
        Result<EmailConfirmationResponse> result = await sender.Send(command, cancellationToken);
        return this.ToActionResult(result);
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        Result<CurrentUserDetails> result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return this.ToActionResult(result);
        }

        return Ok(new CurrentUserResponse(
            result.Value.UserId,
            result.Value.UserName,
            result.Value.Email,
            true));
    }

}

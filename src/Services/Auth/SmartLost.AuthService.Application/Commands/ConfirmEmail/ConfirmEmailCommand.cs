using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Token) : IRequest<Result<EmailConfirmationResponse>>;

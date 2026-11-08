using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Queries.GetCurrentUser;

[Authorize]
public sealed record GetCurrentUserQuery : IRequest<Result<CurrentUserDetails>>;

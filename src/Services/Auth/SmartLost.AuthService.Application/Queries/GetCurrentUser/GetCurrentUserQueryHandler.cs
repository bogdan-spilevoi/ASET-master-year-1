using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserDetails>>
{
    public Task<Result<CurrentUserDetails>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<CurrentUserDetails>.Success(
            new CurrentUserDetails(currentUser.UserId, currentUser.UserName, currentUser.Email)));
    }
}

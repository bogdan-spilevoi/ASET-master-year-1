using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Application.Behaviors;

/// <summary>Stops protected requests before validation or handling when the current user lacks access.</summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResult<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuthorizableRequest authorizableRequest)
        {
            return await next(cancellationToken);
        }

        if (!currentUser.IsAuthenticated)
        {
            return TResponse.Failure([
                new Error("authorization.unauthenticated", "An authenticated user is required.", ErrorKind.Unauthorized)
            ]);
        }

        foreach (AuthorizationRequirement requirement in authorizableRequest.AuthorizationRequirements)
        {
            IReadOnlyList<string> claims = currentUser.GetClaims(requirement.ClaimType);
            bool isSatisfied = requirement.ClaimValue is null
                ? claims.Any(value => !string.IsNullOrWhiteSpace(value))
                : claims.Contains(requirement.ClaimValue, StringComparer.Ordinal);

            if (!isSatisfied)
            {
                return TResponse.Failure([
                    new Error("authorization.forbidden", "The authenticated user does not have the required access.", ErrorKind.Forbidden)
                ]);
            }
        }

        return await next(cancellationToken);
    }
}

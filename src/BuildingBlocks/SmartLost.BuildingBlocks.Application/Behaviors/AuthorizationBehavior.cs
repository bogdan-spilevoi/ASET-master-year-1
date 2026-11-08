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
        AuthorizeAttribute[] attributes = [.. request.GetType().GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()];
        if (attributes.Length == 0)
        {
            return await next(cancellationToken);
        }

        if (!currentUser.IsAuthenticated)
        {
            return TResponse.Failure([
                new Error("authorization.unauthenticated", "An authenticated user is required.", ErrorKind.Unauthorized)
            ]);
        }

        foreach (AuthorizeAttribute attribute in attributes.Where(attribute => attribute.ClaimType is not null))
        {
            IReadOnlyList<string> claims = currentUser.GetClaims(attribute.ClaimType!);
            bool isSatisfied = claims.Contains(attribute.ClaimValue!, StringComparer.Ordinal);

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

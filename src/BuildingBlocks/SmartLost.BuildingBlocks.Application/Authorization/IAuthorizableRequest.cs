namespace SmartLost.BuildingBlocks.Application.Authorization;

/// <summary>Marks a MediatR request that requires an authenticated caller and declared claims.</summary>
public interface IAuthorizableRequest
{
    IReadOnlyList<AuthorizationRequirement> AuthorizationRequirements
    {
        get;
    }
}

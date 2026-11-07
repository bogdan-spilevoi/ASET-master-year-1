namespace SmartLost.BuildingBlocks.Application.Security;

/// <summary>
/// Marks a MediatR request that participates in security auditing.
/// </summary>
public interface ISecurityAuditedRequest
{
    /// <summary>
    /// The classification of the security operation (e.g. "Register", "Login", "Refresh").
    /// </summary>
    string SecurityEventType
    {
        get;
    }

    /// <summary>
    /// The public or sanitized identifier of the subject being acted upon (e.g. username or normalized email).
    /// Sensitive values like raw passwords or tokens must NEVER be returned here.
    /// </summary>
    string? TargetSubject
    {
        get;
    }
}

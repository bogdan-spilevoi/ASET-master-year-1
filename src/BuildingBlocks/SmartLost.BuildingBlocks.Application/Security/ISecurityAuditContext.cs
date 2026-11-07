namespace SmartLost.BuildingBlocks.Application.Security;

/// <summary>
/// Supplies contextual information about the caller initiating the security operation.
/// </summary>
public interface ISecurityAuditContext
{
    string? ClientIp
    {
        get;
    }

    string? UserAgent
    {
        get;
    }
}

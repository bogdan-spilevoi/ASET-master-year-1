namespace SmartLost.BuildingBlocks.Application.Security;

public sealed class DefaultSecurityAuditContext : ISecurityAuditContext
{
    public static readonly DefaultSecurityAuditContext Instance = new();

    public string? ClientIp => null;

    public string? UserAgent => null;
}

using Microsoft.AspNetCore.Http;
using SmartLost.BuildingBlocks.Application.Security;

namespace SmartLost.BuildingBlocks.AspNetCore.Security;

public sealed class HttpSecurityAuditContext(IHttpContextAccessor httpContextAccessor) : ISecurityAuditContext
{
    public string? ClientIp => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
}

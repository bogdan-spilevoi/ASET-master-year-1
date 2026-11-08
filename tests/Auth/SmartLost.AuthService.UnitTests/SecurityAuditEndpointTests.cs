using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Contracts;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class SecurityAuditEndpointTests
{
    [Fact]
    public async Task AuditLogsEndpointReturns401WhenUnauthenticated()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/security/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditLogsEndpointClampsLimitBetweenOneAndHundredForAuthenticatedUser()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        AuthResponse auth = await RegisterAsync(client, "auditlimits", "auditlimits@smartlost.local");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        HttpResponseMessage lowLimit = await client.GetAsync("/api/auth/security/audit-logs?limit=0");
        Assert.Equal(HttpStatusCode.OK, lowLimit.StatusCode);

        HttpResponseMessage highLimit = await client.GetAsync("/api/auth/security/audit-logs?limit=999");
        Assert.Equal(HttpStatusCode.OK, highLimit.StatusCode);
    }

    [Fact]
    public async Task MeEndpointReturns401WhenUnauthenticated()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterAndLoginProduceAuditRecords()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "audituser",
            email = "audit@smartlost.local",
            password = "AuditPass123!"
        });
        Assert.True(registerResponse.IsSuccessStatusCode, $"Register failed: {registerResponse.StatusCode}");

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "audit@smartlost.local",
            password = "AuditPass123!"
        });
        Assert.True(loginResponse.IsSuccessStatusCode, $"Login failed: {loginResponse.StatusCode}");

        AuthResponse auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>() ?? throw new InvalidOperationException("Login response was empty.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        HttpResponseMessage auditResponse = await client.GetAsync("/api/auth/security/audit-logs");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        SecurityAuditLogResponse[]? logs = await auditResponse.Content.ReadFromJsonAsync<SecurityAuditLogResponse[]>();
        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "UserRegistration" && log.SubjectIdentifier == "audituser");
        Assert.Contains(logs, log => log.EventType == "UserLogin" && log.SubjectIdentifier == "audit@smartlost.local");
    }

    [Fact]
    public async Task MeEndpointReturnsUserDetailsWhenAuthenticated()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "meuser",
            email = "meuser@smartlost.local",
            password = "MeUserPass123!"
        });
        Assert.True(registerResponse.IsSuccessStatusCode);
        AuthResponse? auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        HttpResponseMessage meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        CurrentUserResponse? me = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(me);
        Assert.True(me.IsAuthenticated);
        Assert.Equal("meuser", me.UserName);
        Assert.Equal("meuser@smartlost.local", me.Email);
        Assert.NotNull(me.UserId);
    }

    [Fact]
    public async Task OpenApiDocumentIncludesNewEndpoints()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage document = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        using var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        JsonElement paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/auth/me", out _), "Missing /api/auth/me in OpenAPI");
        Assert.True(paths.TryGetProperty("/api/auth/security/audit-logs", out _), "Missing /api/auth/security/audit-logs in OpenAPI");
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string userName, string email)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email,
            password = "AuditPass123!"
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthResponse>() ?? throw new InvalidOperationException("Register response was empty.");
    }
}

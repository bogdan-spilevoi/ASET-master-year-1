using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthDocumentationTests
{
    [Fact]
    public async Task DevelopmentServesSwaggerUiAndItsOpenApiDocument()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Equal("text/html", ui.Content.Headers.ContentType!.MediaType);
        Assert.Contains("SmartLost AuthService API", await ui.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        HttpResponseMessage bundle = await client.GetAsync("/swagger/swagger-ui-bundle.js");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);

        HttpResponseMessage document = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        using var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        JsonElement paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/auth/register", out _));
        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task ProductionDoesNotExposeApiDocumentation(string path)
    {
        using AuthApiFactory factory = new();
        using WebApplicationFactory<Program> productionFactory = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using HttpClient client = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

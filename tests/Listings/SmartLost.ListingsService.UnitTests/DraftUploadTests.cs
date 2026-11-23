using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SkiaSharp;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class DraftUploadTests
{
    [Theory]
    [InlineData("Found", SKEncodedImageFormat.Png, "image/png")]
    [InlineData("Lost", SKEncodedImageFormat.Jpeg, "image/jpeg")]
    public async Task OwnerCanUploadAndOnlyInternalReaderCanRetrieveExactRevision(string type, SKEncodedImageFormat format, string mediaType)
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        var owner = Guid.NewGuid();
        using var bitmap = new SKBitmap(2, 2);
        using SKData image = bitmap.Encode(format, 90);
        byte[] bytes = image.ToArray();
        const string Route = "/api/listings/drafts";
        using HttpResponseMessage anonymous = await client.PostAsync(Route, Form(type, bytes));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var token = new JwtSecurityToken("AuthService", "AuthService", [new Claim("sub", owner.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        using HttpResponseMessage invalid = await client.PostAsync(Route, Form(type, [1, 2, 3]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using HttpResponseMessage empty = await client.PostAsync(Route, Form(type, []));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using HttpResponseMessage response = await client.PostAsync(Route, Form(type, bytes));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DraftCreatedDto draft = (await response.Content.ReadFromJsonAsync<DraftCreatedDto>())!;
        using ListingsDbContext db = factory.CreateDbContext();
        Listing saved = await db.Listings.SingleAsync();
        Assert.Equal(owner, saved.AuthorId);
        Assert.Equal(type, saved.Type.ToString());
        Assert.Equal(ListingState.Draft, saved.State);
        Assert.Equal(draft.ListingId, saved.Id);
        string path = $"/internal/listings/{draft.ListingId}/photos/{draft.PhotoId}/revisions/";
        async Task<HttpContext> Read(int port, int revision, string key) => await factory.Server.SendAsync(context =>
        {
            context.Connection.LocalPort = port;
            context.Request.Path = path + revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Request.Headers["X-Listings-Photo-Key"] = key;
        });
        Assert.Equal(404, (await Read(8080, 1, factory.PhotoReadKey)).Response.StatusCode);
        Assert.Equal(401, (await Read(8082, 1, "incorrect")).Response.StatusCode);
        Assert.Equal(404, (await Read(8082, 2, factory.PhotoReadKey)).Response.StatusCode);
        HttpContext download = await Read(8082, draft.PhotoRevision, factory.PhotoReadKey);
        Assert.Equal(200, download.Response.StatusCode);
        Assert.Equal(mediaType, download.Response.ContentType);
        using var content = new MemoryStream();
        await download.Response.Body.CopyToAsync(content);
        Assert.Equal(bytes, content.ToArray());
    }

    private static MultipartFormDataContent Form(string type, byte[] bytes)
    {
        return new MultipartFormDataContent
        {
            { new StringContent(type), "type" },
            { new ByteArrayContent(bytes), "photo", "photo" }
        };
    }
}

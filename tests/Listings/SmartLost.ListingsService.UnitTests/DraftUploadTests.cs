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

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    public async Task PublicPhotoFollowsListingVisibilityAndProvidesASmallerBoardImage(SKEncodedImageFormat format, string mediaType)
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using ListingsDbContext db = factory.CreateDbContext();
        using var bitmap = new SKBitmap(960, 640);
        using SKData encoded = bitmap.Encode(format, 90);
        byte[] bytes = encoded.ToArray();
        var owner = Guid.NewGuid();
        var listing = Listing.CreateDraft(owner, SmartLost.Contracts.Listings.ListingType.Lost);
        listing.ReplacePhoto(owner, listing.Revision, Photo.Create("test/photo", mediaType, bytes.Length));
        new DraftPhotoStore(db).Add(listing, bytes);
        await db.SaveChangesAsync();
        string url = $"/api/listings/{listing.Id}/photo";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url + "?preview=true")).StatusCode);

        listing.UpdateDetails(owner, listing.Revision, "Backpack", "Blue", "Campus", new DateOnly(2025, 1, 1), DateTime.UtcNow);
        listing.AddCategory(owner, listing.Revision, Category.Create("bags"));
        listing.Publish(owner, listing.Revision, DateTime.UtcNow);
        await db.SaveChangesAsync();
        using HttpResponseMessage full = await client.GetAsync(url);
        full.EnsureSuccessStatusCode();
        Assert.Equal(mediaType, full.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await full.Content.ReadAsByteArrayAsync());
        using HttpResponseMessage preview = await client.GetAsync(url + "?preview=true");
        preview.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", preview.Content.Headers.ContentType?.MediaType);
        using var thumbnail = SKBitmap.Decode(await preview.Content.ReadAsByteArrayAsync());
        Assert.Equal(480, thumbnail.Width);
        Assert.Equal(320, thumbnail.Height);

        listing.Withdraw(owner, listing.Revision);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url + "?preview=true")).StatusCode);
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

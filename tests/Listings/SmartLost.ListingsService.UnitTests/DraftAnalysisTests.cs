using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Api;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class DraftAnalysisTests
{
    [Fact]
    public async Task OnlyOwnerCanRequestAndReadCurrentPhotoAndRepeatedPostsUseOneOutboxRowPerKind()
    {
        using ListingsApiFactory factory = new(seed: false);
        var owner = Guid.NewGuid();
        var draft = Listing.CreateDraft(owner, ListingType.Found);
        var photo = Photo.Create("private/test.png", "image/png", 1);
        draft.ReplacePhoto(owner, draft.Revision, photo);
        using ListingsDbContext db = factory.CreateDbContext();
        new DraftPhotoStore(db).Add(draft, [1]);
        await db.SaveChangesAsync();
        var target = new DraftPhotoTarget(draft.Id, photo.Id, photo.Revision);
        DraftAnalysisDto status = new(target, new("NotRequested"), new("NotRequested"), null);
        bool offline = false;
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(nameof(IMatchingAnalysisReader)).ConfigurePrimaryHttpMessageHandler(() => new Handler(request =>
            {
                Assert.Equal(factory.PhotoReadKey, Assert.Single(request.Headers.GetValues("X-Listings-Photo-Key")));
                Assert.Equal($"/internal/analysis/{draft.Id}/{photo.Id}/1", request.RequestUri!.AbsolutePath);
                return offline ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) :
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
            }))));
        using HttpClient client = host.CreateClient();
        string path = $"/api/listings/drafts/{draft.Id}";
        using HttpResponseMessage anonymous = await client.PostAsync(path + "/traits", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        void SignIn(Guid id)
        {
            var jwt = new JwtSecurityToken("AuthService", "AuthService", [new Claim("sub", id.ToString())],
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        }
        SignIn(Guid.NewGuid());
        using HttpResponseMessage denied = await client.PostAsync(path + "/embedding", null);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using HttpResponseMessage hidden = await client.GetAsync(path + "/analysis");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        SignIn(owner);
        Assert.Equal(status, await client.GetFromJsonAsync<DraftAnalysisDto>(path + "/analysis"));
        foreach (string route in new[] { "/traits", "/embedding", "/traits" })
        {
            using HttpResponseMessage accepted = await client.PostAsync(path + route, null);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.EndsWith(path + "/analysis", accepted.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        }
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
        DraftAnalysisDto? pending = await client.GetFromJsonAsync<DraftAnalysisDto>(path + "/analysis");
        Assert.Equal("Pending", pending!.Traits.State);
        Assert.Equal("Pending", pending.Embedding.State);
        status = status with
        {
            Traits = new("Ready", "qwen"),
            Suggestions = new([], ["colour unclear"])
        };
        DraftAnalysisDto? ready = await client.GetFromJsonAsync<DraftAnalysisDto>(path + "/analysis");
        Assert.Equal("Ready", ready!.Traits.State);
        Assert.Equal("colour unclear", Assert.Single(ready.Suggestions!.Uncertainties));
        offline = true;
        using HttpResponseMessage failure = await client.GetAsync(path + "/analysis");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }
}

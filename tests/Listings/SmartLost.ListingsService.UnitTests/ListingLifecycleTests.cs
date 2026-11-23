using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class ListingLifecycleTests
{
    private static readonly string[] _traits = ["zip"];

    [Fact]
    public async Task PrivatePartialEditsPreserveOmissionsAndPublishWithoutAi()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        JsonElement api = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        JsonElement suggestions = api.GetProperty("paths").GetProperty("/api/listings/{id}/suggestions").GetProperty("get");
        Assert.True(suggestions.GetProperty("responses").TryGetProperty("501", out _));
        Assert.True(suggestions.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        var owner = Guid.NewGuid();
        var draft = Listing.CreateDraft(owner, ListingType.Lost);
        draft.ReplacePhoto(owner, draft.Revision, Photo.Create("private/photo", "image/png", 12));
        var category = Category.Create("Bags");
        using ListingsDbContext db = factory.CreateDbContext();
        db.AddRange(draft, category);
        await db.SaveChangesAsync();
        string path = $"/api/listings/drafts/{draft.Id}";
        SignIn(client, factory, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = 2,
            title = "Other"
        })).StatusCode);
        SignIn(client, factory, owner);
        JsonElement snapshot = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("occurredOn").ValueKind);
        Assert.Equal(draft.Photo!.Id, snapshot.GetProperty("photo").GetProperty("id").GetGuid());
        int rev = snapshot.GetProperty("revision").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/publish", new
        {
            expectedRevision = rev
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = rev
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = rev,
            categoryIds = new[] { Guid.NewGuid() }
        })).StatusCode);
        using HttpResponseMessage first = await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = rev,
            title = "Backpack",
            colour = "blue",
            traits = _traits,
            categoryIds = new[] { category.Id }
        });
        first.EnsureSuccessStatusCode();
        int next = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = rev,
            title = "stale"
        })).StatusCode);
        using HttpResponseMessage second = await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = next,
            description = "Canvas",
            location = "Campus",
            occurredOn = "2025-01-01",
            colour = "",
            traits = Array.Empty<string>(),
            categoryIds = Array.Empty<Guid>()
        });
        second.EnsureSuccessStatusCode();
        snapshot = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Backpack", snapshot.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("colour").ValueKind);
        Assert.Equal(0, snapshot.GetProperty("traits").GetArrayLength());
        next = snapshot.GetProperty("revision").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/publish", new
        {
            expectedRevision = next
        })).StatusCode);
        using HttpResponseMessage final = await client.PatchAsJsonAsync(path, new
        {
            expectedRevision = next,
            categoryIds = new[] { category.Id }
        });
        final.EnsureSuccessStatusCode();
        next = (await final.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(path + "/publish", new
        {
            expectedRevision = next
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/listings/{draft.Id}")).StatusCode);
        Assert.Single(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ManualDecisionsReserveReleaseAndConfirmExactlyOneAward()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using ListingsDbContext db = factory.CreateDbContext();
        var category = Category.Create("Keys");
        Listing lost = Open(ListingType.Lost, category);
        Listing other = Open(ListingType.Lost, category);
        Listing found = Open(ListingType.Found, category);
        Listing rejected = Open(ListingType.Found, category);
        db.AddRange(lost, other, found, rejected);
        await db.SaveChangesAsync();
        Task<HttpResponseMessage> Decide(Listing source, Listing candidate, string choice, int? revision = null) =>
            client.PostAsJsonAsync($"/api/listings/{source.Id}/match-decisions", new
            {
                foundListingId = candidate.Id,
                lostRevision = source.Revision,
                foundRevision = revision ?? candidate.Revision,
                choice
            });

        SignIn(client, factory, lost.AuthorId);
        Assert.Equal(HttpStatusCode.NotImplemented, (await client.GetAsync($"/api/listings/{lost.Id}/suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(lost, other, "Accept")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide(other, found, "Accept")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(lost, found, "Accept", 100)).StatusCode);
        using HttpResponseMessage rejection = await Decide(lost, rejected, "Reject");
        Assert.Equal("Rejected", (await rejection.Content.ReadFromJsonAsync<DecisionDto>())!.State);
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(lost, rejected, "Accept")).StatusCode);
        using HttpResponseMessage accepted = await Decide(lost, found, "Accept");
        accepted.EnsureSuccessStatusCode();
        DecisionDto reservation = (await accepted.Content.ReadFromJsonAsync<DecisionDto>())!;
        string decision = $"/api/match-decisions/{reservation.DecisionId}";
        using HttpResponseMessage replay = await Decide(lost, found, "Accept");
        Assert.Equal(reservation, await replay.Content.ReadFromJsonAsync<DecisionDto>());
        SignIn(client, factory, other.AuthorId);
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(other, found, "Accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(decision + "/confirm-recovery", null)).StatusCode);
        SignIn(client, factory, lost.AuthorId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(decision + "/cancel-acceptance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(decision + "/confirm-recovery", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Decide(lost, found, "Accept")).StatusCode);
        using HttpResponseMessage confirm = await client.PostAsync(decision + "/confirm-recovery", null);
        confirm.EnsureSuccessStatusCode();
        RecoveryDto? recovery = await confirm.Content.ReadFromJsonAsync<RecoveryDto>();
        using HttpResponseMessage repeated = await client.PostAsync(decision + "/confirm-recovery", null);
        Assert.Equal(recovery, await repeated.Content.ReadFromJsonAsync<RecoveryDto>());
        SignIn(client, factory, other.AuthorId);
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(other, found, "Accept")).StatusCode);
        CredibilityEntry award = Assert.Single(await db.CredibilityEntries.AsNoTracking().ToListAsync());
        Assert.Equal(found.AuthorId, award.BeneficiaryId);
        Assert.Equal(recovery!.RecoveryId, award.RecoveryId);
        Assert.Equal(ListingState.Open, (await db.Listings.AsNoTracking().SingleAsync(l => l.Id == found.Id)).State);
        Assert.Equal(ClosureReason.Recovered, (await db.Listings.AsNoTracking().SingleAsync(l => l.Id == lost.Id)).ClosureReason);
    }

    [Fact]
    public async Task WithdrawalInvalidatesReservationWithoutAward()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using ListingsDbContext db = factory.CreateDbContext();
        var category = Category.Create("Bags");
        Listing lost = Open(ListingType.Lost, category);
        Listing found = Open(ListingType.Found, category);
        var decision = MatchDecision.CreateSuggested(lost, found);
        decision.Accept(lost.AuthorId, DateTime.UtcNow);
        db.Add(decision);
        await db.SaveChangesAsync();
        SignIn(client, factory, found.AuthorId);
        using HttpResponseMessage withdrawal = await client.PostAsJsonAsync($"/api/listings/{found.Id}/withdraw", new
        {
            expectedRevision = found.Revision
        });
        Assert.Equal(HttpStatusCode.NoContent, withdrawal.StatusCode);
        Assert.Equal(MatchDecisionState.Invalidated, (await db.MatchDecisions.AsNoTracking().SingleAsync()).State);
        Assert.Empty(await db.CredibilityEntries.ToListAsync());
    }

    private static Listing Open(ListingType type, Category category)
    {
        var owner = Guid.NewGuid();
        var listing = Listing.CreateDraft(owner, type);
        listing.UpdateDetails(owner, listing.Revision, "Keys", "Silver", "Campus", new DateOnly(2025, 1, 1), DateTime.UtcNow);
        listing.AddCategory(owner, listing.Revision, category);
        listing.Publish(owner, listing.Revision, DateTime.UtcNow);
        return listing;
    }

    private static void SignIn(HttpClient client, ListingsApiFactory factory, Guid owner)
    {
        var jwt = new JwtSecurityToken("AuthService", "AuthService", [new Claim("sub", owner.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
    }
}

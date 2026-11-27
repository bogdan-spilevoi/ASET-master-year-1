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
using SmartLost.ListingsService.Api.Contracts;
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
    public async Task CategorySelectionRemainsPrivateUntilPublicationAndReusesTheCatalog()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using ListingsDbContext db = factory.CreateDbContext();
        var owner = Guid.NewGuid();
        SignIn(client, factory, owner);
        Assert.Empty((await client.GetFromJsonAsync<CategoryDto[]>("/api/categories"))!);
        Guid? categoryId = null;
        foreach (string name in new[] { "  Studio   Headphones ", "studio headphones" })
        {
            var listing = Listing.CreateDraft(owner, ListingType.Found);
            db.Add(listing);
            await db.SaveChangesAsync();
            string route = $"/api/listings/drafts/{listing.Id}";
            using HttpResponseMessage patch = await client.PatchAsJsonAsync(route, new
            {
                expectedRevision = 1,
                categoryName = name,
                broadClass = " Electronics "
            });
            patch.EnsureSuccessStatusCode();
            JsonElement saved = await client.GetFromJsonAsync<JsonElement>(route);
            int savedRevision = saved.GetProperty("revision").GetInt32();
            Assert.Equal("studio headphones", saved.GetProperty("draftCategoryName").GetString());
            Assert.Equal("electronics", saved.GetProperty("draftBroadClass").GetString());
            Assert.Equal(0, saved.GetProperty("categories").GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route + "/publish", new ListingRevisionRequest(savedRevision))).StatusCode);
            Assert.Equal(categoryId.HasValue ? 1 : 0, await db.Categories.CountAsync());
            using HttpResponseMessage details = await client.PatchAsJsonAsync(route, new
            {
                expectedRevision = savedRevision,
                title = "Found",
                description = "Wireless",
                location = "Campus",
                occurredOn = "2025-01-01"
            });
            details.EnsureSuccessStatusCode();
            int revision = (await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32();
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(route + "/publish", new ListingRevisionRequest(revision))).StatusCode);
            CategoryDto category = Assert.Single((await client.GetFromJsonAsync<CategoryDto[]>("/api/categories"))!);
            Assert.Equal("electronics", category.BroadClass);
            categoryId ??= category.Id;
            Assert.Equal(categoryId, category.Id);
        }
        Assert.Equal(2, await db.ListingCategories.CountAsync(a => a.CategoryId == categoryId));
    }

    [Fact]
    public async Task OnlyOwnerCanReadDraftPhotoAndDeleteDraftWithItsPayload()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using ListingsDbContext db = factory.CreateDbContext();
        var owner = Guid.NewGuid();
        var draft = Listing.CreateDraft(owner, ListingType.Found);
        byte[] bytes = [1, 2, 3];
        draft.ReplacePhoto(owner, draft.Revision, Photo.Create("private/test", "image/png", bytes.Length));
        new DraftPhotoStore(db).Add(draft, bytes);
        Listing published = Open(ListingType.Found, Category.Create("Books"), owner);
        db.Add(published);
        await db.SaveChangesAsync();

        string path = $"/api/listings/drafts/{draft.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(path)).StatusCode);
        SignIn(client, factory, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + "/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path)).StatusCode);

        SignIn(client, factory, owner);
        using HttpResponseMessage photo = await client.GetAsync(path + "/photo");
        photo.EnsureSuccessStatusCode();
        Assert.Equal("image/png", photo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await photo.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + "/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path)).StatusCode);
        Assert.False(await db.Set<PhotoPayload>().AnyAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/listings/drafts/{published.Id}")).StatusCode);
        Assert.True(await db.Listings.AnyAsync(l => l.Id == published.Id));
    }

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

    [Fact]
    public async Task MyListingsFiltersAndPaginatesOnlyTheAuthenticatedOwnersReports()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using ListingsDbContext db = factory.CreateDbContext();
        const string MinePath = "/api/listings/mine";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(MinePath)).StatusCode);

        var owner = Guid.NewGuid();
        var category = Category.Create("Keys");
        var draft = Listing.CreateDraft(owner, ListingType.Found);
        Listing found = Open(ListingType.Found, category, owner);
        Listing withdrawn = Open(ListingType.Lost, category, owner);
        withdrawn.Withdraw(owner, withdrawn.Revision);
        Listing recovered = Open(ListingType.Lost, category, owner);
        Listing other = Open(ListingType.Found, category);
        var decision = MatchDecision.CreateSuggested(recovered, other);
        decision.Accept(owner, DateTime.UtcNow);
        db.AddRange(draft, found, withdrawn, Recovery.Confirm(decision, owner, DateTime.UtcNow));
        await db.SaveChangesAsync();
        SignIn(client, factory, owner);

        async Task<JsonElement[]> Items(string query, int total)
        {
            JsonElement page = await client.GetFromJsonAsync<JsonElement>(MinePath + query);
            Assert.Equal(total, page.GetProperty("totalCount").GetInt32());
            return page.GetProperty("items").EnumerateArray().ToArray();
        }

        Assert.Equal(4, (await Items("", 4)).Length);
        JsonElement ownDraft = Assert.Single(await Items("?state=Draft", 1));
        Assert.Equal(draft.Id, ownDraft.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, ownDraft.GetProperty("occurredOn").ValueKind);
        Assert.Equal("Open", Assert.Single(await Items("?state=Open", 1)).GetProperty("state").GetString());
        Assert.Equal(2, (await Items("?type=Found", 2)).Length);
        Assert.Equal(2, (await Items("?state=Closed", 2)).Length);
        Assert.Equal("Recovered", Assert.Single(await Items("?closureReason=Recovered", 1)).GetProperty("closureReason").GetString());
        Assert.Equal(found.Id, Assert.Single(await Items("?type=Found&state=Open", 1)).GetProperty("id").GetGuid());
        Assert.Equal("Withdrawn", Assert.Single(await Items("?state=Closed&closureReason=Withdrawn", 1)).GetProperty("closureReason").GetString());
        Assert.Empty(await Items("?type=Found&closureReason=Recovered", 0));
        JsonElement first = Assert.Single(await Items("?pageSize=1", 4));
        JsonElement second = Assert.Single(await Items("?pageSize=1&pageIndex=1", 4));
        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(MinePath + "?state=Unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(MinePath + "?pageSize=2&pageIndex=2147483647")).StatusCode);
        SignIn(client, factory, other.AuthorId);
        Assert.Equal(other.Id, Assert.Single(await Items("", 1)).GetProperty("id").GetGuid());
    }

    private static Listing Open(ListingType type, Category category, Guid? authorId = null)
    {
        Guid owner = authorId ?? Guid.NewGuid();
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

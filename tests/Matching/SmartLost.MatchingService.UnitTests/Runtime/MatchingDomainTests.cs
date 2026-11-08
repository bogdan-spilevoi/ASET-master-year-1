using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingDomainTests
{
    private static readonly EmbeddingVersion _version = new("model", "pre-v2");
    private static readonly DateTime _now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SearchListingNormalizesKeysColourAndLocation()
    {
        Assert.Equal(typeof(ListingType), typeof(SearchListing).GetProperty(nameof(SearchListing.Type))!.PropertyType);
        Assert.Null(typeof(SearchListing).Assembly.GetType("SmartLost.MatchingService.Domain.Enums.ListingType"));
        var photo = new PhotoReference(Guid.NewGuid(), 2);
        var listing = SearchListing.Create(Guid.NewGuid(), 3, 7, ListingType.Found, Guid.NewGuid(), photo,
            [" Backpack ", "backpack", "Bags"], " Black ", ["Zipper", "zipper "], MatchingData.Day, " Library ", true);

        Assert.Equal(3, listing.SourceRevision);
        Assert.Equal(7, listing.ChangeSequence);
        Assert.Equal(ListingType.Found, listing.Type);
        Assert.Equal(photo, listing.CurrentPhoto);
        Assert.Equal(new HashSet<string> { "backpack", "bags" }, listing.CategoryKeys);
        Assert.Equal(new HashSet<string> { "zipper" }, listing.Traits);
        Assert.Equal("black", listing.Colour);
        Assert.Equal("Library", listing.Location);
        Assert.True(listing.Eligible);
        Assert.Null(MatchingData.Listing(ListingType.Lost, colour: null).Colour);
    }

    [Theory]
    [InlineData("listing")]
    [InlineData("revision")]
    [InlineData("sequence")]
    [InlineData("type")]
    [InlineData("author")]
    [InlineData("location")]
    [InlineData("colour")]
    [InlineData("category")]
    [InlineData("longCategory")]
    [InlineData("trait")]
    public void SearchListingRejectsInvalidSnapshots(string field)
    {
        Guid id = field == "listing" ? Guid.Empty : Guid.NewGuid();
        int revision = field == "revision" ? 0 : 1;
        long sequence = field == "sequence" ? -1 : 0;
        ListingType type = field == "type" ? (ListingType)9 : ListingType.Lost;
        Guid author = field == "author" ? Guid.Empty : Guid.NewGuid();
        string location = field == "location" ? " " : "Library";
        string? colour = field == "colour" ? " " : null;
        string[] categories = field switch
        {
            "category" => [" "],
            "longCategory" => [new string('a', 101)],
            _ => ["bag"]
        };
        string[] traits = field == "trait" ? [""] : [];

        Assert.Throws<DomainException>(() => SearchListing.Create(
            id, revision, sequence, type, author, null, categories, colour, traits, MatchingData.Day, location, true));
    }

    [Fact]
    public void PhotoReferenceAndVersionRequireValidValues()
    {
        Assert.Equal(MatchingErrors.PhotoIdRequired, Assert.Throws<DomainException>(() => new PhotoReference(Guid.Empty, 1)).Error);
        Assert.Equal(MatchingErrors.InvalidPhotoRevision,
            Assert.Throws<DomainException>(() => new PhotoReference(Guid.NewGuid(), 0)).Error);
        Assert.Throws<DomainException>(() => new EmbeddingVersion(" ", "pre"));
        Assert.Throws<DomainException>(() => new EmbeddingVersion("model", new string('p', 201)));
        Assert.Equal(new EmbeddingVersion("model", "pre-v2"), _version);
    }

    [Fact]
    public void ImageEmbeddingValidatesNormalizedFiniteVectorsAndComparesCompatibleVersions()
    {
        var photo = new PhotoReference(Guid.NewGuid(), 1);
        var listingId = Guid.NewGuid();
        float[] source = MatchingData.Normalized(1, 1, 0);
        var first = ImageEmbedding.Create(listingId, photo, _version, source);
        source[0] = 0;
        var second = ImageEmbedding.Create(listingId, photo, _version, MatchingData.Normalized(1, 0, 0));
        var otherVersion = ImageEmbedding.Create(listingId, photo, new EmbeddingVersion("model", "pre-v1"),
            MatchingData.Normalized(1, 0, 0));
        var otherDimensions = ImageEmbedding.Create(listingId, photo, _version, MatchingData.Normalized(1, 0));

        Assert.Equal(3, first.Dimensions);
        Assert.NotEqual(0, first.Vector[0]);
        Assert.Equal(listingId, first.ListingId);
        Assert.Equal(photo, first.Photo);
        Assert.Equal(Math.Sqrt(0.5), first.CosineSimilarity(second)!.Value, 5);
        Assert.Null(first.CosineSimilarity(otherVersion));
        Assert.Null(first.CosineSimilarity(otherDimensions));
        Assert.Throws<ArgumentNullException>(() => first.IsCompatibleWith(null!));
    }

    [Fact]
    public void ImageEmbeddingRejectsInvalidInput()
    {
        var photo = new PhotoReference(Guid.NewGuid(), 1);
        Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.Empty, photo, _version, [1f]));
        Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.NewGuid(), null!, _version, [1f]));
        Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.NewGuid(), photo, null!, [1f]));
        Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.NewGuid(), photo, _version, null!));
        Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.NewGuid(), photo, _version, []));
        Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.NewGuid(), photo, _version, [float.NaN]));
        Assert.Equal(MatchingErrors.InvalidEmbeddingVector,
            Assert.Throws<DomainException>(() => ImageEmbedding.Create(Guid.NewGuid(), photo, _version, [0.5f, 0.5f])).Error);
    }

    [Fact]
    public void PreparationTaskCompletesOwnedLease()
    {
        EmbeddingPreparationTask task = NewTask();
        Assert.Equal(EmbeddingPreparationState.Pending, task.State);
        Assert.True(task.IsDue(_now));
        var lease = Guid.NewGuid();

        task.Claim(lease, _now, TimeSpan.FromMinutes(5), 5);
        Assert.Equal(EmbeddingPreparationState.Processing, task.State);
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal(lease, task.LeaseId);
        Assert.False(task.IsDue(_now.AddMinutes(1)));
        Assert.Equal(MatchingErrors.LeaseNotOwned,
            Assert.Throws<DomainException>(() => task.Complete(Guid.NewGuid(), _now.AddMinutes(1))).Error);

        task.Complete(lease, _now.AddMinutes(1));
        Assert.Equal(EmbeddingPreparationState.Ready, task.State);
        Assert.Null(task.LeaseId);
        Assert.Null(task.LeaseExpiresAtUtc);
        Assert.False(task.IsDue(_now.AddDays(1)));
        Assert.Equal(MatchingErrors.TaskAlreadyReady, Assert.Throws<DomainException>(task.MarkObsolete).Error);
    }

    [Fact]
    public void PreparationTaskRetriesThenFailsAfterMaximumAttempts()
    {
        EmbeddingPreparationTask task = NewTask();
        var first = Guid.NewGuid();
        task.Claim(first, _now, TimeSpan.FromMinutes(5), 5);
        task.Fail(first, "matching.inference_timed_out", _now.AddMinutes(1), TimeSpan.FromMinutes(2), 2);

        Assert.Equal(EmbeddingPreparationState.Pending, task.State);
        Assert.Equal("matching.inference_timed_out", task.LastError);
        Assert.Equal(_now.AddMinutes(3), task.NextAttemptAtUtc);
        Assert.False(task.IsDue(_now.AddMinutes(2)));
        Assert.Equal(MatchingErrors.TaskNotDue,
            Assert.Throws<DomainException>(() => task.Claim(Guid.NewGuid(), _now.AddMinutes(2), TimeSpan.FromMinutes(5), 5)).Error);

        var second = Guid.NewGuid();
        task.Claim(second, _now.AddMinutes(3), TimeSpan.FromMinutes(5), 5);
        task.Fail(second, "matching.inference_unavailable", _now.AddMinutes(4), TimeSpan.FromMinutes(2), 2);
        Assert.Equal(EmbeddingPreparationState.Failed, task.State);
        Assert.Equal(2, task.AttemptCount);
        Assert.Null(task.NextAttemptAtUtc);
    }

    [Fact]
    public void ExpiredLeaseCanBeReclaimedButNotCompletedByTheOldWorker()
    {
        EmbeddingPreparationTask task = NewTask();
        var stale = Guid.NewGuid();
        task.Claim(stale, _now, TimeSpan.FromMinutes(1), 5);
        Assert.True(task.IsDue(_now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => task.Complete(stale, _now.AddMinutes(2)));

        task.Claim(Guid.NewGuid(), _now.AddMinutes(2), TimeSpan.FromMinutes(1), 5);
        Assert.Equal(2, task.AttemptCount);
        task.MarkObsolete();
        Assert.Equal(EmbeddingPreparationState.Obsolete, task.State);
        Assert.Null(task.LeaseId);
    }

    [Fact]
    public void PreparationTaskRejectsInvalidArguments()
    {
        var photo = new PhotoReference(Guid.NewGuid(), 1);
        Assert.Throws<DomainException>(() => EmbeddingPreparationTask.Create(Guid.Empty, photo, _version, _now));
        Assert.Throws<DomainException>(() => EmbeddingPreparationTask.Create(Guid.NewGuid(), null!, _version, _now));
        Assert.Throws<DomainException>(() => EmbeddingPreparationTask.Create(Guid.NewGuid(), photo, null!, _now));
        Assert.Equal(MatchingErrors.InvalidUtcTimestamp, Assert.Throws<DomainException>(() =>
            EmbeddingPreparationTask.Create(Guid.NewGuid(), photo, _version, DateTime.SpecifyKind(_now, DateTimeKind.Local))).Error);

        EmbeddingPreparationTask task = NewTask();
        Assert.Throws<DomainException>(() => task.Claim(Guid.Empty, _now, TimeSpan.FromMinutes(1), 5));
        Assert.Throws<DomainException>(() => task.Claim(Guid.NewGuid(), _now, TimeSpan.Zero, 5));
        var lease = Guid.NewGuid();
        task.Claim(lease, _now, TimeSpan.FromMinutes(1), 5);
        Assert.Throws<DomainException>(() => task.Fail(lease, " ", _now, TimeSpan.FromMinutes(1), 1));
        Assert.Throws<DomainException>(() => task.Fail(lease, "code", _now, TimeSpan.Zero, 1));
        Assert.Throws<DomainException>(() => task.Fail(lease, "code", _now, TimeSpan.FromMinutes(1), 0));
        Assert.Throws<DomainException>(() => task.IsDue(default));
    }

    private static EmbeddingPreparationTask NewTask()
    {
        return EmbeddingPreparationTask.Create(Guid.NewGuid(), new PhotoReference(Guid.NewGuid(), 1), _version, _now);
    }
}

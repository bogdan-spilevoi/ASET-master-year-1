using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Suggestions;
using SmartLost.MatchingService.Domain.ValueObjects;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingRankingTests
{
    [Fact]
    public void CandidatePolicyAppliesEligibilityRules()
    {
        var author = Guid.NewGuid();
        SearchListing source = MatchingData.Listing(ListingType.Lost, authorId: author);

        Assert.True(CandidatePolicy.IsCandidate(source, MatchingData.Listing(ListingType.Found)));
        Assert.False(CandidatePolicy.IsCandidate(source, source));
        Assert.False(CandidatePolicy.IsCandidate(source, MatchingData.Listing(ListingType.Lost)));
        Assert.False(CandidatePolicy.IsCandidate(source, MatchingData.Listing(ListingType.Found, authorId: author)));
        Assert.False(CandidatePolicy.IsCandidate(source, MatchingData.Listing(ListingType.Found, eligible: false)));
        Assert.False(CandidatePolicy.IsCandidate(source, MatchingData.Listing(ListingType.Found, categories: ["keys"])));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidatePolicy.IsCandidate(source, source, -1));
    }

    [Fact]
    public void FoundDateMayBeAtMostOneDayBeforeLostDateRegardlessOfSourceType()
    {
        SearchListing lost = MatchingData.Listing(ListingType.Lost);
        SearchListing foundDayBefore = MatchingData.Listing(ListingType.Found, occurredOn: MatchingData.Day.AddDays(-1));
        SearchListing foundTwoDaysBefore = MatchingData.Listing(ListingType.Found, occurredOn: MatchingData.Day.AddDays(-2));
        SearchListing foundLater = MatchingData.Listing(ListingType.Found, occurredOn: MatchingData.Day.AddDays(10));

        Assert.True(CandidatePolicy.IsCandidate(lost, foundDayBefore));
        Assert.False(CandidatePolicy.IsCandidate(lost, foundTwoDaysBefore));
        Assert.True(CandidatePolicy.IsCandidate(lost, foundLater));
        Assert.False(CandidatePolicy.IsCandidate(foundTwoDaysBefore, lost));
        Assert.True(CandidatePolicy.IsCandidate(foundTwoDaysBefore, lost, foundBeforeLostToleranceDays: 2));
        Assert.Equal([foundDayBefore, foundLater],
            CandidatePolicy.Select(lost, [foundDayBefore, foundTwoDaysBefore, foundLater]));
    }

    [Fact]
    public void ScorerExcludesMissingEvidenceAndDerivesReasons()
    {
        SearchListing source = MatchingData.Listing(ListingType.Lost);
        SearchListing candidate = MatchingData.Listing(ListingType.Found, colour: null, location: "Canteen",
            occurredOn: MatchingData.Day.AddDays(1));

        MatchSuggestion withoutVisual = RelevanceScorer.Score(source, candidate, null, RankingPolicy.Baseline);
        double expected = ((0.30 * 1) + (0.15 * 0) + (0.10 * (13d / 14))) / 0.55;
        Assert.Equal(expected, withoutVisual.Relevance, 6);
        Assert.Null(withoutVisual.VisualSimilarity);
        Assert.Equal([MatchReason.SameCategory, MatchReason.CloseDates], withoutVisual.Reasons);
        Assert.Equal("baseline-uncalibrated-v1", withoutVisual.RankingVersion);

        MatchSuggestion withVisual = RelevanceScorer.Score(source, MatchingData.Listing(ListingType.Found), 0.9, RankingPolicy.Baseline);
        Assert.Contains(MatchReason.VisualSimilarity, withVisual.Reasons);
        Assert.Contains(MatchReason.SimilarColour, withVisual.Reasons);
        Assert.Contains(MatchReason.SameArea, withVisual.Reasons);
        Assert.Equal(0, RelevanceScorer.Score(source, candidate, -0.5, RankingPolicy.Baseline with
        {
            CategoryWeight = 0,
            LocationWeight = 0,
            DateWeight = 0,
            ColourWeight = 0
        }).Relevance);
    }

    [Fact]
    public void RankFiltersByThresholdAndSortsDeterministically()
    {
        SearchListing source = MatchingData.Listing(ListingType.Lost);
        SearchListing strong = MatchingData.Listing(ListingType.Found);
        SearchListing weak = MatchingData.Listing(ListingType.Found, categories: ["backpack", "a", "b", "c"], colour: "red",
            location: "Gym", occurredOn: MatchingData.Day.AddDays(30));
        RankingPolicy policy = RankingPolicy.Baseline with
        {
            MinimumRelevance = 0.5
        };

        IReadOnlyList<MatchSuggestion> ranked = RelevanceScorer.Rank(source, [(weak, 0.1), (strong, 0.8)], policy);

        MatchSuggestion only = Assert.Single(ranked);
        Assert.Equal(strong.Id, only.CandidateListingId);
        Assert.Equal(strong.SourceRevision, only.CandidateRevision);
    }

    [Theory]
    [InlineData(" ", 0.3, 0.3, 14)]
    [InlineData("v1", -0.1, 0.3, 14)]
    [InlineData("v1", 0.3, 1.1, 14)]
    [InlineData("v1", 0.3, 0.3, 0)]
    public void RankingPolicyRejectsInvalidSettings(string version, double weight, double threshold, int window)
    {
        RankingPolicy policy = RankingPolicy.Baseline with
        {
            Version = version,
            CategoryWeight = weight,
            MinimumRelevance = threshold,
            DateWindowDays = window
        };
        Assert.Throws<InvalidOperationException>(policy.Validate);
        RankingPolicy.Baseline.Validate();
    }

    [Fact]
    public async Task SelectorAppliesDomainPolicyToRepositoryPool()
    {
        var listings = new FakeListings();
        SearchListing source = MatchingData.Listing(ListingType.Lost);
        SearchListing match = MatchingData.Listing(ListingType.Found);
        listings.Items.AddRange([source, match, MatchingData.Listing(ListingType.Lost)]);

        IReadOnlyList<SearchListing> selected = await new CandidateSelector(listings, new MatchingOptions())
            .SelectAsync(source, CancellationToken.None);

        Assert.Equal([match], selected);
    }

    [Fact]
    public async Task RankerUsesOnlyCompatibleCurrentPhotoEmbeddings()
    {
        var options = new MatchingOptions();
        var sourcePhoto = new PhotoReference(Guid.NewGuid(), 1);
        var samePhoto = new PhotoReference(Guid.NewGuid(), 1);
        var stalePhoto = new PhotoReference(Guid.NewGuid(), 2);
        SearchListing source = MatchingData.Listing(ListingType.Lost, photo: sourcePhoto);
        SearchListing visual = MatchingData.Listing(ListingType.Found, photo: samePhoto);
        SearchListing stale = MatchingData.Listing(ListingType.Found, photo: stalePhoto);
        SearchListing noPhoto = MatchingData.Listing(ListingType.Found);
        var embeddings = new FakeEmbeddings();
        embeddings.Items.AddRange([
            ImageEmbedding.Create(source.Id, sourcePhoto, options.EmbeddingVersion, MatchingData.Unit(4, 0)),
            ImageEmbedding.Create(visual.Id, samePhoto, options.EmbeddingVersion, MatchingData.Unit(4, 0)),
            ImageEmbedding.Create(stale.Id, stalePhoto, new EmbeddingVersion("old", "v1"), MatchingData.Unit(4, 0))
        ]);
        var ranker = new RelevanceRanker(embeddings, options, RankingPolicy.Baseline);

        IReadOnlyList<MatchSuggestion> ranked = await ranker.RankAsync(source, [visual, stale, noPhoto], CancellationToken.None);

        Assert.Equal(1, ranked.Single(item => item.CandidateListingId == visual.Id).VisualSimilarity!.Value, 6);
        Assert.Null(ranked.Single(item => item.CandidateListingId == stale.Id).VisualSimilarity);
        Assert.Null(ranked.Single(item => item.CandidateListingId == noPhoto.Id).VisualSimilarity);

        SearchListing sourceWithoutPhoto = MatchingData.Listing(ListingType.Lost);
        await ranker.RankAsync(sourceWithoutPhoto, [visual], CancellationToken.None);
        Assert.Equal(1, embeddings.Reads);
    }
}

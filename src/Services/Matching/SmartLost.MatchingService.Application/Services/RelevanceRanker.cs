using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Suggestions;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Services;

/// <summary>Ranks candidates using cached vectors only; a cache miss never triggers inference during a query.</summary>
public sealed class RelevanceRanker(IImageEmbeddingRepository embeddings, MatchingOptions options, RankingPolicy policy)
    : IRelevanceRanker
{
    public async Task<IReadOnlyList<MatchSuggestion>> RankAsync(
        SearchListing source, IReadOnlyList<SearchListing> candidates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);
        Dictionary<(Guid ListingId, PhotoReference Photo), ImageEmbedding> vectors = [];
        PhotoReference[] photos = [.. candidates.Prepend(source)
            .Select(listing => listing.CurrentPhoto)
            .OfType<PhotoReference>()
            .Distinct()];
        if (source.CurrentPhoto is not null && photos.Length > 1)
        {
            foreach (ImageEmbedding embedding in await embeddings.GetAsync(photos, options.EmbeddingVersion, cancellationToken))
            {
                if (embedding.Version == options.EmbeddingVersion)
                {
                    vectors.TryAdd((embedding.ListingId, embedding.Photo), embedding);
                }
            }
        }

        ImageEmbedding? sourceVector = Find(vectors, source);
        return RelevanceScorer.Rank(source,
            candidates.Select(candidate => (candidate, Similarity(sourceVector, Find(vectors, candidate)))), policy);
    }

    private static ImageEmbedding? Find(Dictionary<(Guid ListingId, PhotoReference Photo), ImageEmbedding> vectors, SearchListing listing)
    {
        return listing.CurrentPhoto is PhotoReference photo && vectors.TryGetValue((listing.Id, photo), out ImageEmbedding? vector)
            ? vector
            : null;
    }

    private static double? Similarity(ImageEmbedding? source, ImageEmbedding? candidate)
    {
        return source is null || candidate is null ? null : source.CosineSimilarity(candidate);
    }
}

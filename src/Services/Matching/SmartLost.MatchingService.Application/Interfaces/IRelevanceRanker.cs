using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Suggestions;

namespace SmartLost.MatchingService.Application.Interfaces;

public interface IRelevanceRanker
{
    Task<IReadOnlyList<MatchSuggestion>> RankAsync(
        SearchListing source, IReadOnlyList<SearchListing> candidates, CancellationToken cancellationToken);
}

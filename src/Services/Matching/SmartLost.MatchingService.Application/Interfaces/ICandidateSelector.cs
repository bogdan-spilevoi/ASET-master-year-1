using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Application.Interfaces;

public interface ICandidateSelector
{
    Task<IReadOnlyList<SearchListing>> SelectAsync(SearchListing source, CancellationToken cancellationToken);
}

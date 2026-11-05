using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.Suggestions;

namespace SmartLost.MatchingService.Application.Queries.GetMatchSuggestions;

public sealed class GetMatchSuggestionsQueryHandler(
    ISearchListingRepository listings,
    ICandidateSelector selector,
    IRelevanceRanker ranker,
    RankingPolicy policy) : IRequestHandler<GetMatchSuggestionsQuery, Result<MatchSuggestionsResponse>>
{
    public async Task<Result<MatchSuggestionsResponse>> Handle(GetMatchSuggestionsQuery request, CancellationToken cancellationToken)
    {
        SearchListing? source = await listings.GetAsync(request.ListingId, cancellationToken);
        if (source is null || source.SourceRevision < request.ExpectedSourceRevision)
        {
            return Result<MatchSuggestionsResponse>.Success(Outcome(SuggestionStatus.Pending, request.ListingId, source, []));
        }

        if (source.SourceRevision > request.ExpectedSourceRevision)
        {
            return Result<MatchSuggestionsResponse>.Failure(MatchingErrors.SourceRevisionNewer);
        }

        if (!source.Eligible)
        {
            return Result<MatchSuggestionsResponse>.Success(Outcome(SuggestionStatus.Unavailable, request.ListingId, source, []));
        }

        IReadOnlyList<SearchListing> candidates = await selector.SelectAsync(source, cancellationToken);
        IReadOnlyList<MatchSuggestion> ranked = candidates.Count == 0
            ? []
            : await ranker.RankAsync(source, candidates, cancellationToken);
        return Result<MatchSuggestionsResponse>.Success(Outcome(SuggestionStatus.Ready, request.ListingId, source, ranked));
    }

    private MatchSuggestionsResponse Outcome(
        SuggestionStatus status, Guid listingId, SearchListing? source, IReadOnlyList<MatchSuggestion> suggestions)
    {
        return new MatchSuggestionsResponse(status, listingId, source?.SourceRevision,
            status == SuggestionStatus.Ready ? policy.Version : null, suggestions);
    }
}

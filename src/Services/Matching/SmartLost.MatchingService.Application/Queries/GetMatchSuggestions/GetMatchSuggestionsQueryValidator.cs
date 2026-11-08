using FluentValidation;

namespace SmartLost.MatchingService.Application.Queries.GetMatchSuggestions;

public sealed class GetMatchSuggestionsQueryValidator : AbstractValidator<GetMatchSuggestionsQuery>
{
    public GetMatchSuggestionsQueryValidator()
    {
        RuleFor(query => query.ListingId).NotEmpty().WithErrorCode("matching.listing_id_required");
        RuleFor(query => query.ExpectedSourceRevision).GreaterThan(0).WithErrorCode("matching.invalid_source_revision");
    }
}

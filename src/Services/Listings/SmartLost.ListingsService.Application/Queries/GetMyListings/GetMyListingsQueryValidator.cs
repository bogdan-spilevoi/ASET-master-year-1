using FluentValidation;
using SmartLost.BuildingBlocks.Application.Validation;

namespace SmartLost.ListingsService.Application.Queries.GetMyListings;

public sealed class GetMyListingsQueryValidator : BasePaginatedValidator<GetMyListingsQuery>
{
    public GetMyListingsQueryValidator()
    {
        RuleFor(query => query.Type).IsInEnum().WithErrorCode("listings.invalid_type");
        RuleFor(query => query.State).IsInEnum().WithErrorCode("listings.invalid_state");
        RuleFor(query => query.ClosureReason).IsInEnum().WithErrorCode("listings.invalid_closure_reason");
        RuleFor(query => query.PageIndex).Must((query, index) => (long)index * query.PageSize <= int.MaxValue)
            .WithMessage("The requested page offset is too large.").WithErrorCode("pagination.offset_invalid");
    }
}

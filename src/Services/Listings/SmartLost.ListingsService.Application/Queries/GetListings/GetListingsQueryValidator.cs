using FluentValidation;
using SmartLost.BuildingBlocks.Application.Validation;

namespace SmartLost.ListingsService.Application.Queries.GetListings;

public sealed class GetListingsQueryValidator : BasePaginatedValidator<GetListingsQuery>
{
    public GetListingsQueryValidator()
    {
        RuleFor(query => query.Type).IsInEnum().WithErrorCode("listings.invalid_type");
        RuleFor(query => query.CategoryId).NotEqual(Guid.Empty).WithErrorCode("listings.invalid_category_id");
        RuleFor(query => query.PageIndex).Must((query, index) => (long)index * query.PageSize <= int.MaxValue)
            .WithMessage("The requested page offset is too large.").WithErrorCode("pagination.offset_invalid");
    }
}

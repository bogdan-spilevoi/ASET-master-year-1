using FluentValidation;

namespace SmartLost.ListingsService.Application.Queries.GetListingById;

public sealed class GetListingByIdQueryValidator : AbstractValidator<GetListingByIdQuery>
{
    public GetListingByIdQueryValidator()
    {
        RuleFor(query => query.Id).NotEmpty().WithErrorCode("listings.invalid_id");
    }
}

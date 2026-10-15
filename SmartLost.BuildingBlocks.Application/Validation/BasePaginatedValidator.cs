using FluentValidation;
using SmartLost.BuildingBlocks.Core.Pagination;

namespace SmartLost.BuildingBlocks.Application.Validation;

public class BasePaginatedValidator<T> : AbstractValidator<T> where T : IPaginatedRequest
{
    public BasePaginatedValidator()
    {
        RuleFor(request => request.PageIndex)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Page index must be greater than or equal to 0.")
            .WithErrorCode("pagination.page_index_invalid");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PagedDataGuard.MaximumPageSize)
            .WithMessage($"Page size must be between 1 and {PagedDataGuard.MaximumPageSize}.")
            .WithErrorCode("pagination.page_size_invalid");
    }
}

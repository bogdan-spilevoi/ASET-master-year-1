using FluentValidation;

namespace SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;

public sealed class AnalyzeListingPhotoQueryValidator : AbstractValidator<AnalyzeListingPhotoQuery>
{
    public AnalyzeListingPhotoQueryValidator()
    {
        RuleFor(query => query.ListingId).NotEmpty().WithErrorCode("matching.listing_id_required");
        RuleFor(query => query.PhotoId).NotEmpty().WithErrorCode("matching.photo_id_required");
        RuleFor(query => query.PhotoRevision).GreaterThan(0).WithErrorCode("matching.invalid_photo_revision");
    }
}

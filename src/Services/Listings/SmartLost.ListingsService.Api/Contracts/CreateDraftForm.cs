using System.ComponentModel.DataAnnotations;
using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Api.Contracts;

public sealed class CreateDraftForm
{
    [Required]
    public ListingType? Type
    {
        get; init;
    }

    [Required]
    public required IFormFile Photo
    {
        get; init;
    }
}

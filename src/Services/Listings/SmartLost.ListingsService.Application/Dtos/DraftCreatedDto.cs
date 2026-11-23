namespace SmartLost.ListingsService.Application.Dtos;

public sealed record DraftCreatedDto(Guid ListingId, int Revision, Guid PhotoId, int PhotoRevision);

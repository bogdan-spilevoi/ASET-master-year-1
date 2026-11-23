namespace SmartLost.ListingsService.Application.Dtos;

public sealed record DraftPatchDto(int ExpectedRevision, string? Title = null, string? Description = null,
    string? Location = null, DateOnly? OccurredOn = null, string? Colour = null, string[]? Traits = null,
    Guid[]? CategoryIds = null);

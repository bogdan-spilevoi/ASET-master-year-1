namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed record PhotoPayload(Guid PhotoId, byte[] Bytes);

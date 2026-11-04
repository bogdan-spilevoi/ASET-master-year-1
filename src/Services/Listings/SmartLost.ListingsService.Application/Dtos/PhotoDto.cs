namespace SmartLost.ListingsService.Application.Dtos;

public sealed record PhotoDto(Guid Id, string Format, long ByteSize, int Revision);

namespace SmartLost.MatchingService.Application.Contracts;

/// <summary>Photo bytes passed to local inference. Never log the content.</summary>
public sealed record PhotoContent(ReadOnlyMemory<byte> Content, string MediaType);

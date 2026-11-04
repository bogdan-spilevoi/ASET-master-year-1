using System.Security.Cryptography;
using System.Text.Json;

namespace SmartLost.BuildingBlocks.Application.Caching;

public static class QueryCacheKey
{
    public static string Create<TRequest, TResponse>(TRequest request, string prefix) where TRequest : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(request);
        byte[] parameters = JsonSerializer.SerializeToUtf8Bytes(request);
        string fingerprint = Convert.ToHexString(SHA256.HashData(parameters));
        return $"{prefix}:v1:{typeof(TRequest).AssemblyQualifiedName}:{typeof(TResponse).AssemblyQualifiedName}:{fingerprint}";
    }
}

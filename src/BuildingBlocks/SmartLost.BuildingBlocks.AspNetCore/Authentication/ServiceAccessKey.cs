using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SmartLost.BuildingBlocks.AspNetCore.Authentication;

public static class ServiceAccessKey
{
    public static bool Matches(HttpRequest request, string header, string? key)
    {
        return !string.IsNullOrWhiteSpace(key) && request.Headers.TryGetValue(header, out StringValues supplied)
            && supplied.Count == 1 && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString())));
    }
}

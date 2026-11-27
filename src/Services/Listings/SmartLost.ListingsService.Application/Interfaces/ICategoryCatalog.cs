using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface ICategoryCatalog
{
    Task<long> GetVersionAsync(CancellationToken token);
    Task<Category> ResolveAsync(string normalizedName, string? broadClass, CancellationToken token);
}

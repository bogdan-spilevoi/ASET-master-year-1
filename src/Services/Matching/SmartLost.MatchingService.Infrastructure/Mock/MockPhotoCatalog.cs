using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Demo;

namespace SmartLost.MatchingService.Infrastructure.Mock;

/// <summary>Mock photo content used instead of the Listings photo contract.</summary>
public sealed class MockPhotoCatalog
{
    private readonly IReadOnlyDictionary<PhotoReference, string> _appearances;

    public MockPhotoCatalog() : this(MatchingDemoSeeder.DemoPhotos)
    {
    }

    public MockPhotoCatalog(IReadOnlyDictionary<PhotoReference, string> appearances)
    {
        ArgumentNullException.ThrowIfNull(appearances);
        _appearances = appearances;
    }

    public bool TryGet(PhotoReference photo, out string appearance)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return _appearances.TryGetValue(photo, out appearance!);
    }
}

using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Domain.Entities;

/// <summary>
/// Matching-owned search projection derived from a confirmed Listings snapshot.
/// The identifier is the source listing ID; it is a logical reference, not a foreign key.
/// </summary>
public sealed class SearchListing : AggregateRoot<Guid>
{
    public const int MaximumLocationLength = 200;
    public const int MaximumKeyLength = 100;
    public const int MaximumColourLength = 50;

    private SearchListing()
    {
    }

    public int SourceRevision
    {
        get; private set;
    }

    public long ChangeSequence
    {
        get; private set;
    }

    public ListingType Type
    {
        get; private set;
    }

    public Guid AuthorId
    {
        get; private set;
    }

    /// <summary>Current photo pair, or null when the source listing has no photo.</summary>
    public PhotoReference? CurrentPhoto
    {
        get; private set;
    }

    public IReadOnlySet<string> CategoryKeys { get; private set; } = new HashSet<string>();

    public string? Colour
    {
        get; private set;
    }

    public IReadOnlySet<string> Traits { get; private set; } = new HashSet<string>();

    public DateOnly OccurredOn
    {
        get; private set;
    }

    public string Location { get; private set; } = string.Empty;

    /// <summary>Projected Open, unreserved, unconsumed and unrecovered state supplied by Listings.</summary>
    public bool Eligible
    {
        get; private set;
    }

    public static SearchListing Create(
        Guid listingId,
        int sourceRevision,
        long changeSequence,
        ListingType type,
        Guid authorId,
        PhotoReference? currentPhoto,
        IEnumerable<string> categoryKeys,
        string? colour,
        IEnumerable<string> traits,
        DateOnly occurredOn,
        string location,
        bool eligible)
    {
        Guard.Against(listingId == Guid.Empty, MatchingErrors.ListingIdRequired);
        Guard.Against(sourceRevision < 1, MatchingErrors.InvalidSourceRevision);
        Guard.Against(changeSequence < 0, MatchingErrors.InvalidChangeSequence);
        Guard.Against(!Enum.IsDefined(type), MatchingErrors.InvalidListingType);
        Guard.Against(authorId == Guid.Empty, MatchingErrors.AuthorIdRequired);
        Guard.NotNullOrWhiteSpace(location, MatchingErrors.LocationRequired, MaximumLocationLength);
        Guard.Against(colour is not null && (string.IsNullOrWhiteSpace(colour) || colour.Trim().Length > MaximumColourLength),
            MatchingErrors.InvalidColour);

        return new SearchListing
        {
            Id = listingId,
            SourceRevision = sourceRevision,
            ChangeSequence = changeSequence,
            Type = type,
            AuthorId = authorId,
            CurrentPhoto = currentPhoto,
            CategoryKeys = NormalizeKeys(categoryKeys, MatchingErrors.InvalidCategoryKey),
            Colour = colour?.Trim().ToLowerInvariant(),
            Traits = NormalizeKeys(traits, MatchingErrors.InvalidTrait),
            OccurredOn = occurredOn,
            Location = location.Trim(),
            Eligible = eligible
        };
    }

    private static HashSet<string> NormalizeKeys(IEnumerable<string> values, Error error)
    {
        Guard.NotNull(values, error);
        HashSet<string> normalized = new(StringComparer.Ordinal);
        foreach (string value in values)
        {
            Guard.NotNullOrWhiteSpace(value, error);
            string key = value.Trim().ToLowerInvariant();
            Guard.Against(key.Length > MaximumKeyLength, error);
            normalized.Add(key);
        }

        return normalized;
    }
}

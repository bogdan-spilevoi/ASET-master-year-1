using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.ListingsService.Domain.Errors;

/// <summary>Business errors used by the hand-written Domain operations.</summary>
public static class ListingErrors
{
    public static readonly Error InvalidIdentity = new("listings.invalid_identity", "A nonempty identifier is required.", ErrorKind.Validation);
    public static readonly Error RequiredText = new("listings.required_text", "A nonblank value is required.", ErrorKind.Validation);
    public static readonly Error InvalidType = new("listings.invalid_type", "The listing type is invalid.", ErrorKind.Validation);
    public static readonly Error InvalidDate = new("listings.invalid_date", "A non-future occurrence date is required.", ErrorKind.Validation);
    public static readonly Error InvalidTimestamp = new("listings.invalid_timestamp", "A nonempty UTC timestamp is required.", ErrorKind.Validation);
    public static readonly Error NotOwner = new("listings.not_owner", "Only the owner can perform this operation.", ErrorKind.Forbidden);
    public static readonly Error RevisionConflict = new("listings.revision_conflict", "The listing revision has changed.", ErrorKind.Conflict);
    public static readonly Error InvalidState = new("listings.invalid_state", "The current state does not permit this operation.", ErrorKind.Conflict);
    public static readonly Error CounterExhausted = new("listings.counter_exhausted", "The revision or change sequence cannot be advanced.", ErrorKind.Conflict);
    public static readonly Error CategoriesRequired = new("listings.categories_required", "Publication requires at least one confirmed category.", ErrorKind.Validation);
    public static readonly Error InvalidCategory = new("listings.invalid_category", "A category must have an identity and a normalized name.", ErrorKind.Validation);
    public static readonly Error InvalidPhoto = new("listings.invalid_photo", "A private JPEG/PNG photo of at most 10 MiB is required.", ErrorKind.Validation);
    public static readonly Error PhotoAlreadyAttached = new("listings.photo_attached", "Use a new, unattached photo for replacement.", ErrorKind.Conflict);
    public static readonly Error InvalidPair = new("listings.invalid_pair", "A decision requires distinct Open Lost/Found listings from different authors.", ErrorKind.Validation);
    public static readonly Error StaleDecision = new("listings.stale_decision", "The reviewed listing revisions are no longer current.", ErrorKind.Conflict);
    public static readonly Error RecoveryExists = new("listings.recovery_exists", "The decision already has a recovery.", ErrorKind.Conflict);
}

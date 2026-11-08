using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.MatchingService.Domain.Errors;

public static class MatchingErrors
{
    public static readonly Error ListingIdRequired = new(
        "matching.listing_id_required", "A nonempty listing ID is required.", ErrorKind.Validation, "ListingId");

    public static readonly Error AuthorIdRequired = new(
        "matching.author_id_required", "A nonempty author ID is required.", ErrorKind.Validation, "AuthorId");

    public static readonly Error InvalidSourceRevision = new(
        "matching.invalid_source_revision", "The source revision must be positive.", ErrorKind.Validation, "SourceRevision");

    public static readonly Error InvalidChangeSequence = new(
        "matching.invalid_change_sequence", "The change sequence cannot be negative.", ErrorKind.Validation, "ChangeSequence");

    public static readonly Error InvalidListingType = new(
        "matching.invalid_listing_type", "The listing type must be Lost or Found.", ErrorKind.Validation, "Type");

    public static readonly Error LocationRequired = new(
        "matching.location_required", "A nonblank location of at most 200 characters is required.", ErrorKind.Validation, "Location");

    public static readonly Error InvalidCategoryKey = new(
        "matching.invalid_category_key", "Category keys must be nonblank and at most 100 characters.", ErrorKind.Validation, "CategoryKeys");

    public static readonly Error InvalidTrait = new(
        "matching.invalid_trait", "Traits must be nonblank and at most 100 characters.", ErrorKind.Validation, "Traits");

    public static readonly Error InvalidColour = new(
        "matching.invalid_colour", "A colour must be nonblank and at most 50 characters when supplied.", ErrorKind.Validation, "Colour");

    public static readonly Error PhotoIdRequired = new(
        "matching.photo_id_required", "A nonempty photo ID is required.", ErrorKind.Validation, "PhotoId");

    public static readonly Error InvalidPhotoRevision = new(
        "matching.invalid_photo_revision", "The photo revision must be positive.", ErrorKind.Validation, "PhotoRevision");

    public static readonly Error ModelVersionRequired = new(
        "matching.model_version_required", "A nonblank model version of at most 200 characters is required.", ErrorKind.Validation, "ModelVersion");

    public static readonly Error PreprocessingVersionRequired = new(
        "matching.preprocessing_version_required", "A nonblank preprocessing version of at most 200 characters is required.",
        ErrorKind.Validation, "PreprocessingVersion");

    public static readonly Error InvalidEmbeddingVector = new(
        "matching.invalid_embedding_vector", "An embedding must contain finite, L2-normalized components.", ErrorKind.Validation, "Vector");

    public static readonly Error InvalidUtcTimestamp = new(
        "matching.invalid_utc_timestamp", "A nondefault UTC timestamp is required.", ErrorKind.Failure);

    public static readonly Error InvalidDuration = new(
        "matching.invalid_duration", "The duration must be positive.", ErrorKind.Failure);

    public static readonly Error InvalidMaximumAttempts = new(
        "matching.invalid_maximum_attempts", "The maximum number of attempts must be positive.", ErrorKind.Failure);

    public static readonly Error LeaseIdRequired = new(
        "matching.lease_id_required", "A nonempty lease ID is required.", ErrorKind.Failure);

    public static readonly Error ErrorCodeRequired = new(
        "matching.error_code_required", "A nonblank sanitized error code of at most 100 characters is required.", ErrorKind.Failure);

    public static readonly Error TaskNotDue = new(
        "matching.task_not_due", "The embedding preparation task is not due for processing.", ErrorKind.Conflict);

    public static readonly Error AttemptsExhausted = new(
        "matching.attempts_exhausted", "The embedding preparation task has reached its attempt limit.", ErrorKind.Conflict);

    public static readonly Error ConcurrentUpdate = new(
        "matching.concurrent_update", "Matching data changed during the operation. Reload before retrying.", ErrorKind.Conflict);

    public static readonly Error LeaseNotOwned = new(
        "matching.lease_not_owned", "The embedding preparation task is not held by this lease.", ErrorKind.Conflict);

    public static readonly Error TaskAlreadyReady = new(
        "matching.task_already_ready", "A ready embedding preparation task cannot become obsolete.", ErrorKind.Conflict);

    public static readonly Error PreparationTaskNotFound = new(
        "matching.preparation_task_not_found", "The embedding preparation task does not exist.", ErrorKind.NotFound);

    public static readonly Error PhotoUnavailable = new(
        "matching.photo_unavailable", "The requested listing photo is not available.", ErrorKind.NotFound);

    public static readonly Error SourceRevisionNewer = new(
        "matching.source_revision_newer", "Matching holds a newer source revision. Reload the listing before retrying.",
        ErrorKind.Conflict);

    public static readonly Error InferenceUnavailable = new(
        "matching.inference_unavailable", "The local AI runtime could not complete the request.", ErrorKind.Failure);

    public static readonly Error InferenceTimedOut = new(
        "matching.inference_timed_out", "The local AI runtime did not respond within the configured timeout.", ErrorKind.Failure);

    public static readonly Error EmbeddingVersionMismatch = new(
        "matching.embedding_version_mismatch", "The AI runtime returned a different model or preprocessing version.", ErrorKind.Failure);
}

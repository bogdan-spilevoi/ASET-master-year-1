using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class Listing : AggregateRoot<Guid>
{
    private readonly List<string> _traits = [];
    private readonly List<ListingCategory> _categories = [];

    private Listing()
    {
    }

    public Guid AuthorId
    {
        get; private set;
    }

    public ListingType Type { get; private set; } = ListingType.Lost;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    public DateOnly OccurredOn
    {
        get; private set;
    }

    public string? DraftCategoryName
    {
        get; private set;
    }

    public string? DraftBroadClass
    {
        get; private set;
    }

    public string? Colour
    {
        get; private set;
    }

    public IReadOnlyCollection<string> Traits => _traits.AsReadOnly();

    public ListingState State { get; private set; } = ListingState.Draft;

    public ClosureReason? ClosureReason
    {
        get; private set;
    }

    public int Revision { get; private set; } = 1;

    public long ChangeSequence
    {
        get; private set;
    }

    public DateTime? PublishedAt
    {
        get; private set;
    }

    public Photo? Photo
    {
        get; private set;
    }

    public IReadOnlyCollection<ListingCategory> Categories => _categories.AsReadOnly();

    public static Listing CreateDraft(Guid authorId, ListingType type)
    {
        ListingRules.Identity(authorId);
        Guard.Against(!Enum.IsDefined(type), ListingErrors.InvalidType);
        return new Listing { Id = Guid.NewGuid(), AuthorId = authorId, Type = type };
    }

    public void UpdateDetails(Guid actorId, int expectedRevision, string title, string description,
        string location, DateOnly occurredOn, DateTime changedAtUtc, string? colour = null, IEnumerable<string>? traits = null)
    {
        EnsureEditable(actorId, expectedRevision);
        string newTitle = ListingRules.Text(title);
        string newDescription = ListingRules.Text(description);
        string newLocation = ListingRules.Text(location);
        ListingRules.Utc(changedAtUtc);
        Guard.Against(occurredOn == default || occurredOn > DateOnly.FromDateTime(changedAtUtc), ListingErrors.InvalidDate);
        string[] newTraits = traits?.Select(ListingRules.Text).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        Title = newTitle;
        Description = newDescription;
        Location = newLocation;
        OccurredOn = occurredOn;
        Colour = string.IsNullOrWhiteSpace(colour) ? null : colour.Trim();
        _traits.Clear();
        foreach (string trait in newTraits)
        {
            _traits.Add(trait);
        }

        ContentChanged();
    }

    public void PatchDraft(Guid actorId, int expectedRevision, DateTime changedAtUtc, string? title, string? description,
        string? location, DateOnly? occurredOn, string? colour, IReadOnlyList<string>? traits, IReadOnlyList<Category>? categories,
        string? categoryName = null, string? broadClass = null)
    {
        EnsureEditable(actorId, expectedRevision);
        Guard.Against(State != ListingState.Draft, ListingErrors.InvalidState);
        Guard.Against(title is null && description is null && location is null && occurredOn is null &&
            colour is null && traits is null && categories is null && categoryName is null, ListingErrors.InvalidPatch);
        ListingRules.Utc(changedAtUtc);
        Guard.Against(occurredOn.HasValue && (occurredOn == default(DateOnly) || occurredOn > DateOnly.FromDateTime(changedAtUtc)),
            ListingErrors.InvalidDate);

        Guard.Against((categories is not null && categoryName is not null) || (categoryName is null && broadClass is not null), ListingErrors.InvalidCategory);
        string[]? newTraits = traits?.Select(ListingRules.Text).Distinct(StringComparer.Ordinal).ToArray();
        if (categoryName is not null || categories is not null)
        {
            string? normalizedName = string.IsNullOrWhiteSpace(categoryName) ? null : Category.NormalizeName(categoryName);
            DraftBroadClass = normalizedName is null || string.IsNullOrWhiteSpace(broadClass) ? null : Category.NormalizeName(broadClass);
            DraftCategoryName = normalizedName;
            if (categoryName is not null)
            {
                categories = [];
            }
        }
        Title = title?.Trim() ?? Title;
        Description = description?.Trim() ?? Description;
        Location = location?.Trim() ?? Location;
        OccurredOn = occurredOn ?? OccurredOn;
        if (colour is not null)
        {
            Colour = string.IsNullOrWhiteSpace(colour) ? null : colour.Trim();
        }
        if (newTraits is not null)
        {
            _traits.Clear();
            _traits.AddRange(newTraits);
        }
        if (categories is not null)
        {
            var wanted = categories.Select(c => c.Id).ToHashSet();
            foreach (ListingCategory assignment in _categories.Where(c => !wanted.Contains(c.CategoryId)).ToArray())
            {
                _categories.Remove(assignment);
                assignment.Detach();
            }
            foreach (Category category in categories.Where(c => _categories.All(a => a.CategoryId != c.Id)))
            {
                _categories.Add(ListingCategory.Create(this, category));
            }
        }
        ContentChanged();
    }

    public void AddCategory(Guid actorId, int expectedRevision, Category category)
    {
        EnsureEditable(actorId, expectedRevision);
        var assignment = ListingCategory.Create(this, category);
        if (Categories.Any(existing => existing.Category?.Id == category.Id))
        {
            return;
        }

        _categories.Add(assignment);
        ContentChanged();
    }

    public void RemoveCategory(Guid actorId, int expectedRevision, Guid categoryId)
    {
        EnsureEditable(actorId, expectedRevision);
        ListingRules.Identity(categoryId);
        ListingCategory? assignment = Categories.FirstOrDefault(existing => existing.Category?.Id == categoryId);
        if (assignment is not null)
        {
            _categories.Remove(assignment);
            assignment.Detach();
            ContentChanged();
        }
    }

    public void ReplacePhoto(Guid actorId, int expectedRevision, Photo photo)
    {
        EnsureEditable(actorId, expectedRevision);
        Guard.NotNull(photo, ListingErrors.InvalidPhoto);
        Entities.Photo.ValidateMetadata(photo);
        Guard.Against(photo.Listing is not null || photo.ListingId != Guid.Empty, ListingErrors.PhotoAlreadyAttached);
        Photo?.Detach();
        Photo = photo;
        photo.AttachTo(this);
        ContentChanged();
    }

    public void RemovePhoto(Guid actorId, int expectedRevision)
    {
        EnsureEditable(actorId, expectedRevision);
        if (Photo is not null)
        {
            Photo.Detach();
            Photo = null;
            ContentChanged();
        }
    }

    public void Publish(Guid actorId, int expectedRevision, DateTime publishedAtUtc, Category? resolvedCategory = null)
    {
        ListingRules.Owner(AuthorId, actorId);
        EnsureRevision(expectedRevision);
        ListingRules.Utc(publishedAtUtc);
        Guard.Against(State != ListingState.Draft || PublishedAt.HasValue || ClosureReason.HasValue || ChangeSequence != 0,
            ListingErrors.InvalidState);
        ValidatePublication(publishedAtUtc, resolvedCategory);
        if (resolvedCategory is not null)
        {
            _categories.Add(ListingCategory.Create(this, resolvedCategory));
        }
        DraftCategoryName = null;
        DraftBroadClass = null;
        State = ListingState.Open;
        PublishedAt = publishedAtUtc;
        AdvanceSequence();
    }

    public void Withdraw(Guid actorId, int expectedRevision)
    {
        ListingRules.Owner(AuthorId, actorId);
        EnsureRevision(expectedRevision);
        Guard.Against(State != ListingState.Open, ListingErrors.InvalidState);
        ListingRules.SequenceAvailable(this);
        State = ListingState.Closed;
        ClosureReason = Enums.ClosureReason.Withdrawn;
        AdvanceSequence();
    }

    private void EnsureEditable(Guid actorId, int expectedRevision)
    {
        ListingRules.Owner(AuthorId, actorId);
        EnsureRevision(expectedRevision);
        Guard.Against(State is not (ListingState.Draft or ListingState.Open) || ClosureReason.HasValue, ListingErrors.InvalidState);
        Guard.Against(Revision == int.MaxValue, ListingErrors.CounterExhausted);
        ListingRules.SequenceAvailable(this);
    }

    private void EnsureRevision(int expectedRevision)
    {
        ListingRules.Identity(Id);
        Guard.Against(Revision < 1 || expectedRevision != Revision, ListingErrors.RevisionConflict);
    }

    private void ContentChanged()
    {
        Revision++;
        if (State == ListingState.Open)
        {
            AdvanceSequence();
        }
    }

    internal void AdvanceSequence()
    {
        ChangeSequence++;
        AddDomainEvent(new Events.ListingChanged(Id, Revision, ChangeSequence, State));
    }

    internal void MarkRecovered()
    {
        State = ListingState.Closed;
        ClosureReason = Enums.ClosureReason.Recovered;
    }

    private void ValidatePublication(DateTime publishedAtUtc, Category? resolvedCategory)
    {
        Guard.Against(!Enum.IsDefined(Type), ListingErrors.InvalidType);
        _ = ListingRules.Text(Title);
        _ = ListingRules.Text(Description);
        _ = ListingRules.Text(Location);
        Guard.Against(OccurredOn == default || OccurredOn > DateOnly.FromDateTime(publishedAtUtc), ListingErrors.InvalidDate);
        Guard.Against((Categories.Count == 0 || DraftCategoryName is not null) && resolvedCategory is null, ListingErrors.CategoriesRequired);
        HashSet<Guid> categoryIds = resolvedCategory is null ? [] : [resolvedCategory.Id];
        foreach (ListingCategory assignment in Categories)
        {
            Guard.Against(assignment.Category is null || assignment.Category.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(assignment.Category.NormalizedName) || !categoryIds.Add(assignment.Category.Id),
                ListingErrors.InvalidCategory);
        }

        if (Photo is not null)
        {
            Entities.Photo.ValidateMetadata(Photo);
        }
    }
}

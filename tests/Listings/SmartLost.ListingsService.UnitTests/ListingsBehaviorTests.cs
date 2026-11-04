using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed partial class ListingsBehaviorTests
{
    private static readonly DateTime _now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CategoryFactoryAndRenameNormalizeCaseAndWhitespace()
    {
        var category = Category.Create("  Travel\t BAG  ");
        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal("travel bag", category.NormalizedName);
        Guid id = category.Id;
        category.Rename(" BACKPACK ");
        Assert.Equal(id, category.Id);
        Assert.Equal("backpack", category.NormalizedName);
        Fails(ListingErrors.RequiredText, () => category.Rename(" \t"));
        Assert.Equal("backpack", category.NormalizedName);
        Fails(ListingErrors.RequiredText, () => Category.Create(null!));
    }

    [Theory]
    [InlineData("image/jpeg", 1)]
    [InlineData(" IMAGE/PNG ", Photo.MaximumByteSize)]
    public void PhotoFactoryCreatesValidatedMetadata(string format, long size)
    {
        var photo = Photo.Create(" private/photo ", format, size);
        Assert.NotEqual(Guid.Empty, photo.Id);
        Assert.Equal("private/photo", photo.BlobKey);
        Assert.Equal(format.Trim().ToLowerInvariant(), photo.Format);
        Assert.Equal(size, photo.ByteSize);
        Assert.Equal(1, photo.Revision);
        Assert.Null(photo.Listing);
    }

    [Theory]
    [InlineData("image/gif", 100)]
    [InlineData("image/png", 0)]
    [InlineData("image/png", -1)]
    [InlineData("image/jpeg", Photo.MaximumByteSize + 1)]
    public void PhotoFactoryRejectsUnsupportedMetadata(string format, long size)
    {
        Fails(ListingErrors.InvalidPhoto, () => Photo.Create("private/photo", format, size));
        Fails(ListingErrors.RequiredText, () => Photo.Create(" ", "image/png", 1));
        Fails(ListingErrors.RequiredText, () => Photo.Create("private/photo", null!, 1));
    }

    [Fact]
    public void DraftCreationAndDetailsDoNotPublishDraftEvents()
    {
        var author = Guid.NewGuid();
        var listing = Listing.CreateDraft(author, ListingType.Found);
        Assert.NotEqual(Guid.Empty, listing.Id);
        Assert.Equal(author, listing.AuthorId);
        Assert.Equal(ListingType.Found, listing.Type);
        Assert.Equal(ListingState.Draft, listing.State);
        Assert.Equal(1, listing.Revision);
        Assert.Equal(0, listing.ChangeSequence);
        listing.UpdateDetails(author, 1, " Bag ", " Blue bag ", " Library ",
            new DateOnly(2026, 11, 3), _now, " Blue ", [" black strap ", "black strap", "logo"]);
        Assert.Equal("Bag", listing.Title);
        Assert.Equal("Blue bag", listing.Description);
        Assert.Equal("Library", listing.Location);
        Assert.Equal("Blue", listing.Colour);
        Assert.Equal(["black strap", "logo"], listing.Traits);
        Assert.Equal(new DateOnly(2026, 11, 3), listing.OccurredOn);
        Assert.Equal(2, listing.Revision);
        Assert.Equal(0, listing.ChangeSequence);
        Assert.Empty(listing.DomainEvents);
        listing.UpdateDetails(author, 2, "Bag", "Blue bag", "Library", new DateOnly(2026, 11, 3), _now);
        Assert.Null(listing.Colour);
        Assert.Empty(listing.Traits);
        Fails(ListingErrors.InvalidIdentity, () => Listing.CreateDraft(Guid.Empty, ListingType.Lost));
        Fails(ListingErrors.InvalidType, () => Listing.CreateDraft(author, (ListingType)99));
    }

    [Fact]
    public void InvalidDetailsAndStaleOrForeignEditsDoNotPartiallyChangeTheListing()
    {
        Listing listing = Draft();
        string title = listing.Title;
        int revision = listing.Revision;
        void Update(Guid actor, int expected, string description, DateOnly date, string[]? traits = null)
        {
            listing.UpdateDetails(actor, expected, "Changed", description, "Hall", date, _now, traits: traits);
        }

        Fails(ListingErrors.NotOwner, () => Update(Guid.NewGuid(), revision, "Text", new DateOnly(2026, 11, 3)));
        Fails(ListingErrors.NotOwner, () => Update(Guid.Empty, revision, "Text", new DateOnly(2026, 11, 3)));
        Fails(ListingErrors.RevisionConflict, () => Update(listing.AuthorId, revision - 1, "Text", new DateOnly(2026, 11, 3)));
        Fails(ListingErrors.RequiredText, () => Update(listing.AuthorId, revision, " ", new DateOnly(2026, 11, 3)));
        Fails(ListingErrors.InvalidDate, () => Update(listing.AuthorId, revision, "Text", default));
        Fails(ListingErrors.InvalidDate, () => Update(listing.AuthorId, revision, "Text", new DateOnly(2026, 11, 5)));
        Fails(ListingErrors.RequiredText, () => Update(listing.AuthorId, revision, "Text", new DateOnly(2026, 11, 3), ["valid", " "]));
        Assert.Equal(title, listing.Title);
        Assert.Equal(revision, listing.Revision);
    }

    [Fact]
    public void CategoryAssignmentsAreUniqueByIdentityAndMaintainContainment()
    {
        var listing = Listing.CreateDraft(Guid.NewGuid(), ListingType.Lost);
        var category = Category.Create("bag");
        listing.AddCategory(listing.AuthorId, 1, category);
        ListingCategory assignment = Assert.Single(listing.Categories);
        Assert.Same(listing, assignment.Listing);
        Assert.Same(category, assignment.Category);
        Assert.Equal(2, listing.Revision);
        listing.AddCategory(listing.AuthorId, 2, category);
        Assert.Single(listing.Categories);
        Assert.Equal(2, listing.Revision);
        listing.RemoveCategory(listing.AuthorId, 2, Guid.NewGuid());
        Assert.Equal(2, listing.Revision);
        listing.RemoveCategory(listing.AuthorId, 2, category.Id);
        Assert.Empty(listing.Categories);
        Assert.Null(assignment.Listing);
        Assert.Equal(3, listing.Revision);
        Fails(ListingErrors.InvalidCategory, () => listing.AddCategory(listing.AuthorId, 3, Blank<Category>()));
        Fails(ListingErrors.InvalidCategory, () => listing.AddCategory(listing.AuthorId, 3, null!));
        Fails(ListingErrors.InvalidIdentity, () => listing.RemoveCategory(listing.AuthorId, 3, Guid.Empty));
    }

    [Fact]
    public void PhotoReplacementAndRemovalKeepInverseLinksAndAdvanceContentRevision()
    {
        Listing listing = Draft();
        var first = Photo.Create("private/first", "image/png", 1);
        listing.ReplacePhoto(listing.AuthorId, listing.Revision, first);
        Assert.Same(listing, first.Listing);
        int revision = listing.Revision;
        Fails(ListingErrors.PhotoAlreadyAttached, () => listing.ReplacePhoto(listing.AuthorId, revision, first));
        var replacement = Photo.Create("private/second", "image/jpeg", 2);
        listing.ReplacePhoto(listing.AuthorId, revision, replacement);
        Assert.Same(replacement, listing.Photo);
        Assert.Same(listing, replacement.Listing);
        Assert.Null(first.Listing);
        listing.RemovePhoto(listing.AuthorId, listing.Revision);
        Assert.Null(listing.Photo);
        Assert.Null(replacement.Listing);
        revision = listing.Revision;
        listing.RemovePhoto(listing.AuthorId, revision);
        Assert.Equal(revision, listing.Revision);
        Fails(ListingErrors.InvalidPhoto, () => listing.ReplacePhoto(listing.AuthorId, revision, null!));
        Fails(ListingErrors.InvalidIdentity, () => listing.ReplacePhoto(listing.AuthorId, revision, Blank<Photo>()));
        var rooted = Photo.Create("private/root", "image/png", 1);
        Listing another = Draft();
        another.ReplacePhoto(another.AuthorId, another.Revision, rooted);
        Fails(ListingErrors.PhotoAlreadyAttached, () => listing.ReplacePhoto(listing.AuthorId, revision, rooted));
    }

    [Fact]
    public void PublishStartsDeliverySequenceAndOpenEditsAdvanceItSeparately()
    {
        Listing listing = Draft();
        int revision = listing.Revision;
        listing.Publish(listing.AuthorId, revision, _now);
        Assert.Equal(ListingState.Open, listing.State);
        Assert.Equal(_now, listing.PublishedAt);
        Assert.Equal(revision, listing.Revision);
        Assert.Equal(1, listing.ChangeSequence);
        listing.UpdateDetails(listing.AuthorId, revision, "Updated", "Text", "Hall", new DateOnly(2026, 11, 3), _now);
        Assert.Equal(revision + 1, listing.Revision);
        Assert.Equal(2, listing.ChangeSequence);
        listing.Withdraw(listing.AuthorId, listing.Revision);
        Assert.Equal(ListingState.Closed, listing.State);
        Assert.Equal(ClosureReason.Withdrawn, listing.ClosureReason);
        Assert.Equal(3, listing.ChangeSequence);
        Fails(ListingErrors.InvalidState, () => listing.Publish(listing.AuthorId, listing.Revision, _now));
        Fails(ListingErrors.InvalidState, () => listing.Withdraw(listing.AuthorId, listing.Revision));
        Fails(ListingErrors.InvalidState, () => listing.RemovePhoto(listing.AuthorId, listing.Revision));
    }

    [Fact]
    public void PublishRejectsIncompleteFutureOrCorruptedDraftsWithoutOpeningThem()
    {
        var empty = Listing.CreateDraft(Guid.NewGuid(), ListingType.Lost);
        Fails(ListingErrors.RequiredText, () => empty.Publish(empty.AuthorId, 1, _now));
        Listing listing = Draft();
        listing.RemoveCategory(listing.AuthorId, listing.Revision, Assert.Single(listing.Categories).CategoryId);
        Fails(ListingErrors.CategoriesRequired, () => Publish(listing));
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("bag"));
        Set(listing, nameof(Listing.OccurredOn), DateOnly.FromDateTime(_now.AddDays(1)));
        Fails(ListingErrors.InvalidDate, () => Publish(listing));
        Set(listing, nameof(Listing.OccurredOn), default(DateOnly));
        Fails(ListingErrors.InvalidDate, () => Publish(listing));
        Set(listing, nameof(Listing.OccurredOn), DateOnly.FromDateTime(_now));
        Category category = Assert.Single(listing.Categories).Category;
        Set(category, nameof(Category.NormalizedName), " ");
        Fails(ListingErrors.InvalidCategory, () => Publish(listing));
        Set(category, nameof(Category.NormalizedName), "bag");
        var invalidPhoto = Photo.Create("private/bad", "image/png", 1);
        Set(invalidPhoto, nameof(Photo.Format), "image/gif");
        Set(listing, nameof(Listing.Photo), invalidPhoto);
        Fails(ListingErrors.InvalidPhoto, () => Publish(listing));
        Set(listing, "Photo", null!);
        Set(listing, "Type", (ListingType)99);
        Fails(ListingErrors.InvalidType, () => Publish(listing));
        Set(listing, "Type", ListingType.Lost);
        Fails(ListingErrors.InvalidTimestamp, () => listing.Publish(listing.AuthorId, listing.Revision, DateTime.SpecifyKind(_now, DateTimeKind.Unspecified)));
        Fails(ListingErrors.InvalidTimestamp, () => listing.Publish(listing.AuthorId, listing.Revision, default));
        Set(listing, "ChangeSequence", 1);
        Fails(ListingErrors.InvalidState, () => Publish(listing));
        Assert.Equal(ListingState.Draft, listing.State);
        Assert.Null(listing.PublishedAt);
    }

    [Fact]
    public void RevisionAndSequenceOverflowAreRejectedBeforeMutation()
    {
        Listing listing = Open();
        string title = listing.Title;
        Set(listing, "Revision", int.MaxValue);
        Fails(ListingErrors.CounterExhausted, () => listing.UpdateDetails(listing.AuthorId, int.MaxValue,
            "Other", "Text", "Hall", new DateOnly(2026, 11, 3), _now));
        Assert.Equal(title, listing.Title);
        Set(listing, "Revision", 1);
        Set(listing, "ChangeSequence", long.MaxValue);
        Fails(ListingErrors.CounterExhausted, () => listing.Withdraw(listing.AuthorId, 1));
        Assert.Equal(ListingState.Open, listing.State);
        Set(listing, "ChangeSequence", -1);
        Fails(ListingErrors.CounterExhausted, () => listing.RemovePhoto(listing.AuthorId, 1));
        Set(listing, "ChangeSequence", 1);
        Set(listing, "Revision", 0);
        Fails(ListingErrors.RevisionConflict, () => listing.Withdraw(listing.AuthorId, 0));
        Listing draft = Draft();
        Fails(ListingErrors.InvalidState, () => draft.Withdraw(draft.AuthorId, draft.Revision));
    }

    private static T Blank<T>() where T : class
    {
        return (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
    }

    private static void Set(object entity, string property, object? value)
    {
        entity.GetType().GetProperty(property)!.SetValue(entity, value);
    }

    private static Listing Draft(ListingType type = ListingType.Lost)
    {
        var listing = Listing.CreateDraft(Guid.NewGuid(), type);
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Bag", "A blue bag", "Library", new DateOnly(2026, 11, 3), _now);
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("bag"));
        return listing;
    }

    private static Listing Open(ListingType type = ListingType.Lost)
    {
        Listing listing = Draft(type);
        Publish(listing);
        return listing;
    }

    private static void Publish(Listing listing)
    {
        listing.Publish(listing.AuthorId, listing.Revision, _now);
    }

    private static void Fails(Error expected, Action operation)
    {
        Assert.Equal(expected, Assert.Throws<DomainException>(operation).Error);
    }
}

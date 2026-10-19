using NMF.Expressions;
using NMF.Models;
using NMF.Models.Collections;
using NMF.Models.Meta;
using SmartLost.ListingsService.Modeling.Listings;
using Xunit;
using Category = SmartLost.ListingsService.Modeling.Listings.Category;

namespace SmartLost.ListingsService.UnitTests;

public sealed partial class ListingsModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListingCollectionAdaptersMaintainContainmentAndReportChanges(bool references)
    {
        Listing listing = new();
        ReferenceCollection collection = references
            ? new Listing.ListingReferencedElementsCollection(listing)
            : new Listing.ListingChildrenCollection(listing);
        ReferenceCollection items = collection;
        INotifyCollection<IModelElement> observable = collection.AsNotifiable();
        int changes = 0;
        observable.CollectionChanged += (_, _) => changes++;
        observable.Successors.SetDummy();
        try
        {
            Assert.Empty(collection);
            Assert.Empty(items);
            Photo photo = new();
            ListingCategory assignment = new();
            Category unrelated = new();
            items.Add(photo);
            items.Add(assignment);
            items.Add(unrelated);
            Assert.Equal(2, items.Count);
            Assert.Same(photo, listing.Photo);
            Assert.Same(listing, assignment.Listing);
            Assert.Contains(photo, items);
            Assert.Contains(assignment, items);
            Assert.DoesNotContain(unrelated, items);
            Assert.False(items.Remove(unrelated));

            var copy = new IModelElement[3];
            items.CopyTo(copy, 1);
            Assert.Null(copy[0]);
            Assert.Same(photo, copy[1]);
            Assert.Same(assignment, copy[2]);
            List<IModelElement> enumerated = [];
            foreach (IModelElement item in collection)
            {
                enumerated.Add(item);
            }

            Assert.Equal(2, enumerated.Count);
            Assert.True(items.Remove(photo));
            Assert.Null(listing.Photo);
            Assert.True(items.Remove(assignment));
            Assert.Null(assignment.Listing);
            Photo finalPhoto = new();
            ListingCategory finalAssignment = new();
            items.Add(finalPhoto);
            items.Add(finalAssignment);
            items.Clear();
            Assert.Empty(items);
            Assert.Null(finalPhoto.Listing);
            Assert.Null(finalAssignment.Listing);
            Assert.True(changes > 0);
        }
        finally
        {
            observable.Successors.UnsetAll();
        }
    }

    [Fact]
    public void PhotoReferenceAdapterSupportsReadingAndClearingItsOwner()
    {
        Photo photo = new();
        Photo.PhotoReferencedElementsCollection items = new(photo);
        INotifyCollection<IModelElement> observable = items.AsNotifiable();
        observable.Successors.SetDummy();
        try
        {
            Assert.Empty(items);
            Assert.Empty(items);
            Listing listing = new()
            {
                Photo = photo
            };
            Assert.Single(items);
            Assert.Contains(listing, items);
            Assert.DoesNotContain(new Category(), items);
            var copy = new IModelElement[1];
            items.CopyTo(copy, 0);
            Assert.Same(listing, copy[0]);
            using IEnumerator<IModelElement> enumerator = items.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            Assert.Same(listing, enumerator.Current);
            Assert.False(enumerator.MoveNext());
            Assert.False(items.Remove(new Listing()));
            Assert.True(items.Remove(listing));
            Assert.Null(photo.Listing);
            Photo replacement = new();
            Photo.PhotoReferencedElementsCollection replacementItems = new(replacement);
            listing.Photo = replacement;
            replacementItems.Clear();
            Assert.Empty(replacementItems);
            Assert.Null(listing.Photo);
        }
        finally
        {
            observable.Successors.UnsetAll();
        }
    }

    [Fact]
    public void AssignmentReferenceAdapterMaintainsOwnerAndCategorySeparately()
    {
        ListingCategory assignment = new();
        ListingCategory.ListingCategoryReferencedElementsCollection items = new(assignment);
        INotifyCollection<IModelElement> observable = items.AsNotifiable();
        observable.Successors.SetDummy();
        try
        {
            Assert.Empty(items);
            Assert.Empty(items);
            Listing listing = new();
            Category category = new();
            items.Add(listing);
            items.Add(category);
            items.Add(new Photo());
            Assert.Equal(2, items.Count);
            Assert.Same(listing, assignment.Listing);
            Assert.Same(category, assignment.Category);
            Assert.Contains(listing, items);
            Assert.Contains(category, items);
            Assert.DoesNotContain(new Photo(), items);
            var copy = new IModelElement[2];
            items.CopyTo(copy, 0);
            Assert.Same(listing, copy[0]);
            Assert.Same(category, copy[1]);
            List<IModelElement> enumerated = [];
            foreach (IModelElement item in items)
            {
                enumerated.Add(item);
            }

            Assert.Equal(2, enumerated.Count);
            Assert.False(items.Remove(new Photo()));
            Assert.True(items.Remove(category));
            Assert.Null(assignment.Category);
            Assert.True(items.Remove(listing));
            Assert.Null(assignment.Listing);
            ListingCategory replacement = new();
            ListingCategory.ListingCategoryReferencedElementsCollection replacementItems = new(replacement)
            {
                listing,
                category
            };
            replacementItems.Clear();
            Assert.Empty(replacementItems);
            Assert.Empty(listing.Categories);
        }
        finally
        {
            observable.Successors.UnsetAll();
        }
    }

    [Fact]
    public void ReferenceExpressionsAndReflectiveAccessObserveTheSameObjects()
    {
        InspectableListing listing = new();
        AssertReference<IPhoto>(listing, "photo", new Photo(), new Photo());
        InspectableAssignment assignment = new();
        AssertReference<ICategory>(assignment, "category", new Category(), new Category());
        AssertReference<IListing>(assignment, "listing", new Listing(), new Listing());

        InspectablePhoto photo = new();
        IReference owner = photo.GetClass().References.Single(reference => reference.Name == "listing");
        INotifyExpression<IModelElement> expression = photo.ReferenceExpression("LISTING");
        expression.Successors.SetDummy();
        try
        {
            Assert.Null(expression.Value);
            listing.Photo = photo;
            Assert.Same(listing, expression.Value);
            Assert.Same(listing, photo.GetReferencedElement(owner));
            INotifyReversableExpression<IListing> reverse = Assert.IsType<INotifyReversableExpression<IListing>>(expression, exactMatch: false);
            reverse.Value = null!;
            Assert.Null(photo.Listing);
            Assert.Null(listing.Photo);
            Photo replacement = new();
            listing.Photo = replacement;
            replacement.SetReferencedElement(owner, null!);
            Assert.Null(replacement.Listing);
            Assert.Null(listing.Photo);
        }
        finally
        {
            expression.Successors.UnsetAll();
        }
    }

    [Fact]
    public void TraitsAndCategoryDeletionRaiseObservableChanges()
    {
        Listing listing = new();
        listing.BubbledChange += (_, _) => { };
        IAttribute traits = listing.GetClass().Attributes.Single(attribute => attribute.Name == "traits");
        System.Collections.IList values = listing.GetAttributeValues(traits);
        values.Add("yellow keyring");
        Assert.Contains("yellow keyring", listing.Traits);
        values.Remove("yellow keyring");
        Assert.Empty(listing.Traits);

        Category category = new();
        ListingCategory assignment = new();
        int changing = 0;
        int changed = 0;
        assignment.CategoryChanging += (_, _) => changing++;
        assignment.CategoryChanged += (_, _) => changed++;
        assignment.ListingChanging += (_, _) => changing++;
        assignment.ListingChanged += (_, _) => changed++;
        assignment.BubbledChange += (_, _) => { };
        assignment.Category = category;
        listing.Categories.Add(assignment);
        IReference categories = listing.GetClass().References.Single(reference => reference.Name == "categories");
        Assert.Same(assignment, listing.GetReferencedElements(categories)[0]);
        Assert.Same(assignment, listing.GetReferencedElement(categories, 0));
        Assert.Null(listing.GetReferencedElement(categories, 10));
        category.Delete();
        Assert.Null(assignment.Category);
        assignment.Listing = null!;
        Assert.Empty(listing.Categories);
        Assert.True(changing >= 3);
        Assert.True(changed >= changing);
    }

    private static void AssertReference<T>(ModelElement element, string name, T first, T second)
        where T : class, IModelElement
    {
        var inspection = (IModelInspection)element;
        IReference reference = element.GetClass().References.Single(reference => reference.Name == name);
        INotifyExpression<IModelElement> expression = inspection.ReferenceExpression(name.ToUpperInvariant());
        expression.Successors.SetDummy();
        try
        {
            INotifyReversableExpression<T> reverse = Assert.IsType<INotifyReversableExpression<T>>(expression, exactMatch: false);
            reverse.Value = first;
            Assert.Same(first, element.GetReferencedElement(reference));
            Assert.Same(first, expression.Value);
            if (name != "listing")
            {
                reverse.Value = null!;
                Assert.Null(expression.Value);
            }
            element.SetReferencedElement(reference, second);
            Assert.Same(second, element.GetReferencedElement(reference));
            Assert.Same(second, expression.Value);
            reverse.Value = null!;
            Assert.Null(expression.Value);
        }
        finally
        {
            expression.Successors.UnsetAll();
        }
    }
}

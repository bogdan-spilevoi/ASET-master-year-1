using System.Reflection;
using NMF.Expressions;
using NMF.Models;
using NMF.Models.Meta;
using NMF.Models.Repository;
using SmartLost.ListingsService.Modeling.Listings;
using Xunit;
using Category = SmartLost.ListingsService.Modeling.Listings.Category;

namespace SmartLost.ListingsService.UnitTests;

public sealed partial class ListingsModelTests
{
    [Fact]
    public void DefaultsAndMetamodelPreserveThePublicationStructure()
    {
        Listing listing = new();
        Assert.Equal(ListingType.Lost, listing.Type);
        Assert.Equal(ListingState.Draft, listing.State);
        Assert.Equal(1, listing.Revision);
        Assert.Equal(0L, listing.ChangeSequence);
        Assert.Null(listing.ClosureReason);
        Assert.Null(listing.PublishedAt);
        Assert.Null(listing.Photo);
        Assert.Empty(listing.Categories);
        Assert.Empty(listing.Traits);
        Assert.Equal(1, new Photo().Revision);

        IClass type = listing.GetClass();
        Assert.Same(Listing.ClassInstance, type);
        Assert.Equal("Listing", type.Name);
        IReference photo = type.References.Single(reference => reference.Name == "photo");
        IReference categories = type.References.Single(reference => reference.Name == "categories");
        Assert.True(photo.IsContainment);
        Assert.Equal(0, photo.LowerBound);
        Assert.Equal(1, photo.UpperBound);
        Assert.True(categories.IsContainment);
        Assert.Equal(-1, categories.UpperBound);
        Assert.Equal("listing", photo.Opposite.Name);
        Assert.Equal("listing", categories.Opposite.Name);

        IClass assignment = new ListingCategory().GetClass();
        IReference category = assignment.References.Single(reference => reference.Name == "category");
        Assert.False(category.IsContainment);
        Assert.Equal(1, category.LowerBound);
        Assert.Equal("Category", category.ReferenceType.Name);
        Assert.Same(Category.ClassInstance, new Category().GetClass());
        Assert.Same(Photo.ClassInstance, new Photo().GetClass());
        Assert.Same(ListingCategory.ClassInstance, assignment);
    }

    [Fact]
    public void SampleLoadsSharedCategoriesAndSurvivesAnXmiRoundTrip()
    {
        ModelRepository repository = new();
        Model model = repository.Resolve(Path.Combine(AppContext.BaseDirectory, "Models", "campus-backpacks.xmi"));
        AssertSample(model);

        string path = Path.Combine(Path.GetTempPath(), $"smartlost-listings-{Guid.NewGuid():N}.xmi");
        try
        {
            repository.Save(model, path);
            ModelRepository reloadedRepository = new();
            Model reloaded = reloadedRepository.Resolve(path);
            AssertSample(reloaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Listing")]
    [InlineData("Photo")]
    [InlineData("Category")]
    [InlineData("MatchDecision")]
    [InlineData("Recovery")]
    [InlineData("CredibilityEntry")]
    public void ScalarAttributesSupportReflectiveAccessAndObservableUpdates(string className)
    {
        ModelElement element = CreateInspectable(className);
        var inspection = (IModelInspection)element;
        List<string?> changes = [];
        element.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        element.BubbledChange += (_, _) => { };

        foreach (IAttribute attribute in element.GetClass().Attributes.Where(attribute => attribute.UpperBound == 1))
        {
            object first = AttributeValue(attribute.Name, false, className);
            object second = AttributeValue(attribute.Name, true, className);
            string propertyName = char.ToUpperInvariant(attribute.Name[0]) + attribute.Name[1..];
            ValueChangedEventArgs? changing = null;
            ValueChangedEventArgs? changed = null;
            EventHandler<ValueChangedEventArgs> onChanging = (_, args) => changing = args;
            EventHandler<ValueChangedEventArgs> onChanged = (_, args) => changed = args;
            EventInfo? changingEvent = element.GetType().GetEvent(propertyName + "Changing");
            EventInfo? changedEvent = element.GetType().GetEvent(propertyName + "Changed");
            Assert.NotNull(changingEvent);
            Assert.NotNull(changedEvent);
            changingEvent.AddEventHandler(element, onChanging);
            changedEvent.AddEventHandler(element, onChanged);
            INotifyExpression<object> expression = inspection.AttributeExpression(attribute.Name.ToUpperInvariant());
            expression.Successors.SetDummy();
            try
            {
                element.SetAttributeValue(attribute, first);
                Assert.Equal(first, element.GetAttributeValue(attribute));
                Assert.Equal(first, expression.Value);

                if (expression is INotifyReversableExpression<string> text)
                {
                    text.Value = (string)second;
                }
                else
                {
                    INotifyReversableExpression<object> reversible = Assert.IsType<INotifyReversableExpression<object>>(expression, exactMatch: false);
                    reversible.Value = second;
                }
                Assert.Equal(second, element.GetAttributeValue(attribute));
                Assert.Equal(second, expression.Value);
                Assert.NotNull(changing);
                Assert.NotNull(changed);
                Assert.Equal(first, changing.OldValue);
                Assert.Equal(second, changing.NewValue);
                Assert.Equal(first, changed.OldValue);
                Assert.Equal(second, changed.NewValue);
                int count = changes.Count;
                element.SetAttributeValue(attribute, second);
                Assert.Equal(count, changes.Count);
            }
            finally
            {
                expression.Successors.UnsetAll();
                changingEvent.RemoveEventHandler(element, onChanging);
                changedEvent.RemoveEventHandler(element, onChanged);
            }
        }

        Assert.NotEmpty(changes);
        Assert.All(element.GetClass().Attributes.Where(attribute => attribute.UpperBound == 1),
            attribute => Assert.Contains(attribute.Name, changes, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhotoReplacementAndAssignmentMovesMaintainContainment()
    {
        Listing first = new()
        {
            Id = Guid.NewGuid()
        };
        Listing second = new()
        {
            Id = Guid.NewGuid()
        };
        Photo photo = new()
        {
            Id = Guid.NewGuid()
        };
        Category category = new()
        {
            Id = Guid.NewGuid(),
            NormalizedName = "backpack"
        };
        ListingCategory assignment = new()
        {
            Category = category
        };

        first.Photo = photo;
        Assert.Same(first, photo.Listing);
        first.Photo = null!;
        Assert.Null(photo.Listing);
        Photo replacement = new()
        {
            Id = Guid.NewGuid()
        };
        second.Photo = replacement;
        Assert.Null(first.Photo);
        Assert.Same(replacement, second.Photo);
        Assert.Same(second, replacement.Parent);

        first.Categories.Add(assignment);
        Assert.Same(first, assignment.Listing);
        assignment.Listing = second;
        Assert.Empty(first.Categories);
        Assert.Same(assignment, Assert.Single(second.Categories));
        Assert.Same(category, assignment.Category);
        Assert.Null(category.Parent);

        second.Categories.Remove(assignment);
        Assert.Null(assignment.Listing);
        Assert.Same(category, assignment.Category);
        replacement.Listing = null!;
        Assert.Null(second.Photo);
    }

    [Fact]
    public void IdentityResolutionTracksChangesAndMovesBetweenModels()
    {
        Model first = new()
        {
            ModelUri = new Uri("https://smartlost.example/samples/first.xmi")
        };
        Model second = new()
        {
            ModelUri = new Uri("https://smartlost.example/samples/second.xmi")
        };
        ModelElement[] elements =
        [
            new Listing { Id = Guid.NewGuid() },
            new Photo { Id = Guid.NewGuid() },
            new Category { Id = Guid.NewGuid() },
            new MatchDecision { Id = Guid.NewGuid() },
            new Recovery { Id = Guid.NewGuid() },
            new CredibilityEntry { Id = Guid.NewGuid() }
        ];

        foreach (ModelElement element in elements)
        {
            first.RootElements.Add(element);
            string previousId = element.ToIdentifierString();
            Assert.Same(element, first.Resolve(previousId));
            Assert.True(element.IsIdentified);
            Assert.Contains(previousId, element.RelativeUri.ToString(), StringComparison.Ordinal);
            Assert.Contains(previousId, element.AbsoluteUri.ToString(), StringComparison.Ordinal);

            IAttribute id = element.GetClass().Attributes.Single(attribute => attribute.Name == "id");
            element.SetAttributeValue(id, Guid.NewGuid());
            Assert.Null(first.Resolve(previousId));
            Assert.Same(element, first.Resolve(element.ToIdentifierString()));

            second.RootElements.Add(element);
            Assert.Same(second, element.Model);
            Assert.Null(first.Resolve(element.ToIdentifierString()));
            Assert.Same(element, second.Resolve(element.ToIdentifierString()));
            second.RootElements.Remove(element);
            Assert.Null(element.Model);
        }
    }

    private static void AssertSample(Model model)
    {
        Assert.Equal(4, model.RootElements.Count);
        var categories = model.RootElements.OfType<Category>().ToDictionary(category => category.Id);
        Listing[] listings = [.. model.RootElements.OfType<Listing>()];
        Assert.Equal(2, categories.Count);
        Assert.Equal(2, listings.Length);
        Listing lost = Assert.Single(listings, listing => listing.Type == ListingType.Lost);
        Listing found = Assert.Single(listings, listing => listing.Type == ListingType.Found);
        Assert.NotEqual(lost.AuthorId, found.AuthorId);
        Assert.All(listings, listing =>
        {
            Assert.Equal(ListingState.Open, listing.State);
            Assert.Equal(1, listing.Revision);
            Assert.Equal(1L, listing.ChangeSequence);
            Assert.Null(listing.ClosureReason);
            Assert.NotNull(listing.PublishedAt);
            Assert.Equal(2, listing.Categories.Count);
            Assert.All(listing.Categories, assignment =>
            {
                Assert.Same(listing, assignment.Listing);
                Assert.Same(categories[assignment.Category.Id], assignment.Category);
            });
        });
        Assert.NotNull(lost.Photo);
        Assert.Same(lost, lost.Photo.Listing);
        Assert.Equal("image/jpeg", lost.Photo.Format);
        Assert.Equal(245760, lost.Photo.ByteSize);
        Assert.Null(found.Photo);
        Assert.Contains("black front pocket", lost.Traits);
        Assert.Contains("yellow keyring", lost.Traits);
    }

    private static ModelElement CreateInspectable(string className)
    {
        return className switch
        {
            "Listing" => new InspectableListing(),
            "Photo" => new InspectablePhoto(),
            "Category" => new InspectableCategory(),
            "MatchDecision" => new InspectableMatchDecision(),
            "Recovery" => new InspectableRecovery(),
            "CredibilityEntry" => new InspectableCredibilityEntry(),
            _ => throw new ArgumentException("Unsupported model class.", nameof(className))
        };
    }

    private static object AttributeValue(string name, bool alternate, string className)
    {
        return name switch
        {
            "id" or "authorId" or "decidedBy" or "confirmedBy" or "beneficiaryId" or "helpedUserId" => alternate
                ? Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")
                : Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            "type" => alternate ? ListingType.Lost : ListingType.Found,
            "state" when className == "MatchDecision" => alternate ? MatchDecisionState.Confirmed : MatchDecisionState.Accepted,
            "state" => alternate ? ListingState.Closed : ListingState.Open,
            "closureReason" => alternate ? ClosureReason.Withdrawn : ClosureReason.Recovered,
            "revision" or "lostRevision" or "foundRevision" or "points" => alternate ? 3 : 2,
            "byteSize" => alternate ? 2048L : 1024L,
            "changeSequence" => alternate ? 3000000002L : 3000000001L,
            "occurredOn" or "publishedAt" or "decidedAt" or "confirmedAt" or "awardedAt" => new DateTime(2026, 10, alternate ? 2 : 1, 0, 0, 0, DateTimeKind.Utc),
            _ => alternate ? "updated value" : "initial value"
        };
    }

    private interface IModelInspection
    {
        INotifyExpression<object> AttributeExpression(string name);
        INotifyExpression<IModelElement> ReferenceExpression(string name);
    }

    private sealed class InspectableListing : Listing, IModelInspection
    {
        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }
    }

    private sealed class InspectablePhoto : Photo, IModelInspection
    {
        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }
    }

    private sealed class InspectableCategory : Category, IModelInspection
    {
        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }
    }

    private sealed class InspectableAssignment : ListingCategory, IModelInspection
    {
        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }

        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }
    }
}

using System.Reflection;
using NMF.Expressions;
using NMF.Models;
using NMF.Models.Meta;
using NMF.Models.Repository;
using SmartLost.MatchingService.Modeling.Matching;
using Xunit;

namespace SmartLost.MatchingService.UnitTests;

public sealed partial class MatchingModelTests
{
    private static readonly string[] _modelVersions = ["model-v1", "model-v2"];
    private static readonly float[] _components = [0.5f, -0.5f, 0.5f, -0.5f];
    [Fact]
    public void MetamodelUsesIndependentEntitiesAndAnOrderedNonUniqueVector()
    {
        SearchListing listing = new();
        ImageEmbedding embedding = new();
        Assert.Equal(ListingType.Lost, listing.Type);
        Assert.Equal(1, listing.SourceRevision);
        Assert.False(listing.Eligible);
        Assert.Null(listing.CurrentPhotoId);
        Assert.Null(listing.CurrentPhotoRevision);
        foreach (string name in new[] { "currentPhotoId", "currentPhotoRevision" })
        {
            IAttribute attribute = listing.GetClass().Attributes.Single(attribute => attribute.Name == name);
            Assert.Equal(0, attribute.LowerBound);
            Assert.Equal(1, attribute.UpperBound);
        }
        Assert.Equal(1, embedding.PhotoRevision);
        Assert.Same(SearchListing.ClassInstance, listing.GetClass());
        Assert.Same(ImageEmbedding.ClassInstance, embedding.GetClass());
        IReference reference = embedding.GetClass().References.Single();
        Assert.Equal("searchListing", reference.Name);
        Assert.False(reference.IsContainment);
        Assert.Equal(1, reference.UpperBound);
        Assert.Equal(1, reference.LowerBound);
        Assert.Null(reference.Opposite);
        IAttribute vector = embedding.GetClass().Attributes.Single(attribute => attribute.Name == "vector");
        Assert.Equal(1, vector.LowerBound);
        Assert.Equal(-1, vector.UpperBound);
        Assert.True(vector.IsOrdered);
        Assert.False(vector.IsUnique);
    }

    [Fact]
    public void XmiRoundTripPreservesVersionsReferencesAndRepeatedVectorComponents()
    {
        SearchListing listing = new()
        {
            ListingId = Guid.NewGuid(),
            AuthorId = Guid.NewGuid(),
            CurrentPhotoId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            CurrentPhotoRevision = 2,
            Type = ListingType.Found,
            SourceRevision = 3,
            ChangeSequence = 7,
            Eligible = true,
            Location = "Campus library",
            OccurredOn = new DateTime(2026, 10, 1),
            Colour = "Blue"
        };
        listing.CategoryKeys.Add("backpack");
        listing.CategoryKeys.Add("shoulder bag");
        listing.Traits.Add("yellow keyring");
        ImageEmbedding first = CreateEmbedding(listing, "model-v1");
        ImageEmbedding second = CreateEmbedding(listing, "model-v2");
        Model model = new();
        model.RootElements.Add(listing);
        model.RootElements.Add(first);
        model.RootElements.Add(second);
        string path = Path.Combine(Path.GetTempPath(), $"smartlost-matching-{Guid.NewGuid():N}.xmi");
        try
        {
            new ModelRepository().Save(model, path);
            Model loaded = new ModelRepository().Resolve(path);
            Assert.Equal(3, loaded.RootElements.Count);
            SearchListing copy = Assert.Single(loaded.RootElements.OfType<SearchListing>());
            Assert.Equal(listing.ListingId, copy.ListingId);
            Assert.Equal(listing.AuthorId, copy.AuthorId);
            Assert.Equal(listing.CurrentPhotoId, copy.CurrentPhotoId);
            Assert.Equal(listing.CurrentPhotoRevision, copy.CurrentPhotoRevision);
            Assert.Equal(3, copy.SourceRevision);
            Assert.Equal(7, copy.ChangeSequence);
            Assert.Equal(ListingType.Found, copy.Type);
            Assert.True(copy.Eligible);
            Assert.Equal("Campus library", copy.Location);
            Assert.Equal(listing.OccurredOn, copy.OccurredOn);
            Assert.Equal("Blue", copy.Colour);
            Assert.Contains("shoulder bag", copy.CategoryKeys);
            Assert.Contains("yellow keyring", copy.Traits);
            ImageEmbedding[] embeddings = [.. loaded.RootElements.OfType<ImageEmbedding>()];
            Assert.Equal(2, embeddings.Length);
            Assert.Equal(_modelVersions, embeddings.Select(item => item.ModelVersion).Order());
            Assert.All(embeddings, item =>
            {
                Assert.Same(copy, item.SearchListing);
                Assert.Equal(first.PhotoId, item.PhotoId);
                Assert.Equal(2, item.PhotoRevision);
                Assert.Equal(copy.CurrentPhotoId, item.PhotoId);
                Assert.Equal(copy.CurrentPhotoRevision, item.PhotoRevision);
                Assert.Equal("preprocessing-v1", item.PreprocessingVersion);
                Assert.Equal(4, item.Dimensions);
                Assert.Equal(_components, item.Vector);
                Assert.Same(loaded, item.Parent);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProjectionReferencesChangeWithoutChangingModelContainment()
    {
        SearchListing first = new();
        SearchListing second = new();
        ImageEmbedding embedding = new()
        {
            SearchListing = first
        };
        Assert.Same(first, embedding.SearchListing);
        Assert.Null(embedding.Parent);
        embedding.SearchListing = second;
        Assert.Same(second, embedding.SearchListing);
        Assert.Null(first.Parent);
        embedding.SearchListing = null!;
        Assert.Null(embedding.SearchListing);
        embedding.SearchListing = first;
        first.Delete();
        Assert.Null(embedding.SearchListing);
    }

    [Theory]
    [InlineData("SearchListing")]
    [InlineData("ImageEmbedding")]
    [InlineData("EmbeddingPreparationTask")]
    public void ScalarAttributesSupportReflectiveAccessAndObservableUpdates(string className)
    {
        ModelElement element = CreateInspectable(className);
        var inspection = (IModelInspection)element;
        List<string?> changes = [];
        element.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        element.BubbledChange += (_, _) => { };

        foreach (IAttribute attribute in element.GetClass().Attributes.Where(attribute => attribute.UpperBound == 1))
        {
            object first = AttributeValue(attribute.Name, false);
            object second = AttributeValue(attribute.Name, true);
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


    [Theory]
    [InlineData("SearchListing")]
    [InlineData("ImageEmbedding")]
    [InlineData("EmbeddingPreparationTask")]
    public void IdentityResolutionTracksUpdatesAndMovesBetweenModels(string className)
    {
        ModelElement element = CreateInspectable(className);
        Model first = new()
        {
            ModelUri = new Uri("https://smartlost.example/samples/matching-first.xmi")
        };
        Model second = new()
        {
            ModelUri = new Uri("https://smartlost.example/samples/matching-second.xmi")
        };
        IAttribute id = element.GetClass().Attributes.Single(attribute => attribute.Name == (className == "SearchListing" ? "listingId" : "id"));
        element.SetAttributeValue(id, Guid.NewGuid());
        first.RootElements.Add(element);
        string previousId = element.ToIdentifierString();
        Assert.True(element.IsIdentified);
        Assert.Same(element, first.Resolve(previousId));
        Assert.Contains(previousId, element.RelativeUri.ToString(), StringComparison.Ordinal);
        Assert.Contains(previousId, element.AbsoluteUri.ToString(), StringComparison.Ordinal);
        element.SetAttributeValue(id, Guid.NewGuid());
        Assert.Null(first.Resolve(previousId));
        second.RootElements.Add(element);
        Assert.Same(second, element.Model);
        Assert.Null(first.Resolve(element.ToIdentifierString()));
        Assert.Same(element, second.Resolve(element.ToIdentifierString()));
        second.RootElements.Remove(element);
        Assert.Null(element.Model);
    }

    private static ImageEmbedding CreateEmbedding(SearchListing listing, string version)
    {
        ImageEmbedding embedding = new()
        {
            Id = Guid.NewGuid(),
            SearchListing = listing,
            PhotoId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            PhotoRevision = 2,
            ModelVersion = version,
            PreprocessingVersion = "preprocessing-v1",
            Dimensions = 4
        };
        foreach (float component in _components)
        {
            embedding.Vector.Add(component);
        }
        return embedding;
    }

    private static ModelElement CreateInspectable(string className)
    {
        return className switch
        {
            "SearchListing" => new InspectableSearchListing(),
            "ImageEmbedding" => new InspectableImageEmbedding(),
            "EmbeddingPreparationTask" => new InspectableEmbeddingPreparationTask(),
            _ => throw new ArgumentException("Unsupported model class.", nameof(className))
        };
    }

    private static object AttributeValue(string name, bool alternate)
    {
        return name switch
        {
            "id" or "listingId" or "authorId" or "photoId" or "currentPhotoId" or "leaseId" => alternate
                ? Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")
                : Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            "type" => alternate ? ListingType.Lost : ListingType.Found,
            "sourceRevision" or "photoRevision" or "currentPhotoRevision" or "dimensions" or "attemptCount" => alternate ? 3 : 2,
            "state" => alternate ? EmbeddingPreparationState.Ready : EmbeddingPreparationState.Processing,
            "changeSequence" => alternate ? 3L : 2L,
            "eligible" => !alternate,
            "occurredOn" or "nextAttemptAt" or "leaseExpiresAt" => new DateTime(2026, 10, alternate ? 2 : 1, 0, 0, 0, DateTimeKind.Utc),
            _ => alternate ? "updated value" : "initial value"
        };
    }

    private interface IModelInspection
    {
        INotifyExpression<object> AttributeExpression(string name);
        INotifyExpression<IModelElement> ReferenceExpression(string name);
    }

    private sealed class InspectableSearchListing : SearchListing, IModelInspection
    {
        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }
        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }
    }

    private sealed class InspectableImageEmbedding : ImageEmbedding, IModelInspection
    {
        public INotifyExpression<object> AttributeExpression(string name)
        {
            return GetExpressionForAttribute(name);
        }
        public INotifyExpression<IModelElement> ReferenceExpression(string name)
        {
            return GetExpressionForReference(name);
        }
    }
}

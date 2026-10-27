using NMF.Expressions;
using NMF.Models;
using NMF.Models.Meta;
using NMF.Models.Repository;
using SmartLost.MatchingService.Modeling.Matching;
using Xunit;

namespace SmartLost.MatchingService.UnitTests;

public sealed partial class MatchingModelTests
{
    [Fact]
    public void PreparationTaskMetadataPreservesItsIndependentIdentityAndDefaults()
    {
        EmbeddingPreparationTask task = new();
        Assert.Same(EmbeddingPreparationTask.ClassInstance, task.GetClass());
        Assert.Equal(EmbeddingPreparationState.Pending, task.State);
        Assert.Equal(0, task.AttemptCount);
        Assert.Equal(1, task.PhotoRevision);
        Assert.Null(task.NextAttemptAt);
        Assert.Null(task.LeaseId);
        Assert.Null(task.LeaseExpiresAt);
        Assert.Null(task.LastError);
        Assert.Empty(task.Children);
        Assert.Equal(5, Enum.GetValues<EmbeddingPreparationState>().Length);
        IReference reference = Assert.Single(task.GetClass().References);
        Assert.Equal("searchListing", reference.Name);
        Assert.Equal("SearchListing", reference.ReferenceType.Name);
        Assert.False(reference.IsContainment);
        Assert.Null(reference.Opposite);
        Assert.Equal(1, reference.LowerBound);
        Assert.Equal(1, reference.UpperBound);
        foreach (string name in new[] { "nextAttemptAt", "leaseId", "leaseExpiresAt", "lastError" })
        {
            IAttribute attribute = task.GetClass().Attributes.Single(attribute => attribute.Name == name);
            Assert.Equal(0, attribute.LowerBound);
            Assert.Equal(1, attribute.UpperBound);
        }
    }

    [Theory]
    [InlineData(EmbeddingPreparationState.Pending)]
    [InlineData(EmbeddingPreparationState.Processing)]
    [InlineData(EmbeddingPreparationState.Ready)]
    [InlineData(EmbeddingPreparationState.Failed)]
    [InlineData(EmbeddingPreparationState.Obsolete)]
    public void PreparationTaskStatesAndProcessingTargetSurviveAnXmiRoundTrip(EmbeddingPreparationState state)
    {
        SearchListing listing = new()
        {
            ListingId = Guid.NewGuid(),
            AuthorId = Guid.NewGuid(),
            SourceRevision = 2,
            ChangeSequence = 3000000001L,
            CurrentPhotoId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            CurrentPhotoRevision = 2,
            Location = "Campus library",
            OccurredOn = new DateTime(2026, 10, 1),
            Eligible = true
        };
        ImageEmbedding embedding = CreateEmbedding(listing, "model-v1");
        EmbeddingPreparationTask task = new()
        {
            Id = Guid.NewGuid(),
            SearchListing = listing,
            PhotoId = embedding.PhotoId,
            PhotoRevision = embedding.PhotoRevision,
            ModelVersion = embedding.ModelVersion,
            PreprocessingVersion = embedding.PreprocessingVersion,
            State = state,
            AttemptCount = state == EmbeddingPreparationState.Pending ? 0 : 1,
            NextAttemptAt = state == EmbeddingPreparationState.Pending ? new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc) : null,
            LeaseId = state == EmbeddingPreparationState.Processing ? Guid.NewGuid() : null,
            LeaseExpiresAt = state == EmbeddingPreparationState.Processing ? new DateTime(2026, 10, 2, 10, 5, 0, DateTimeKind.Utc) : null,
            LastError = state == EmbeddingPreparationState.Failed ? "embedding.attempt_limit_reached" : null!
        };
        if (state == EmbeddingPreparationState.Obsolete)
        {
            listing.CurrentPhotoId = Guid.NewGuid();
            listing.CurrentPhotoRevision = 1;
        }

        Model model = new();
        model.RootElements.Add(listing);
        model.RootElements.Add(task);
        if (state == EmbeddingPreparationState.Ready)
        {
            model.RootElements.Add(embedding);
        }

        string path = Path.Combine(Path.GetTempPath(), $"smartlost-embedding-task-{Guid.NewGuid():N}.xmi");
        try
        {
            new ModelRepository().Save(model, path);
            Model loaded = new ModelRepository().Resolve(path);
            SearchListing copy = Assert.Single(loaded.RootElements.OfType<SearchListing>());
            EmbeddingPreparationTask restored = Assert.Single(loaded.RootElements.OfType<EmbeddingPreparationTask>());
            Assert.Same(copy, restored.SearchListing);
            Assert.Same(loaded, restored.Parent);
            Assert.Equal(task.Id, restored.Id);
            Assert.Equal(task.PhotoId, restored.PhotoId);
            Assert.Equal(task.PhotoRevision, restored.PhotoRevision);
            Assert.Equal(task.ModelVersion, restored.ModelVersion);
            Assert.Equal(task.PreprocessingVersion, restored.PreprocessingVersion);
            Assert.Equal(state, restored.State);
            Assert.Equal(task.AttemptCount, restored.AttemptCount);
            Assert.Equal(task.NextAttemptAt, restored.NextAttemptAt);
            Assert.Equal(task.LeaseId, restored.LeaseId);
            Assert.Equal(task.LeaseExpiresAt, restored.LeaseExpiresAt);
            Assert.Equal(task.LastError, restored.LastError);
            Assert.Equal(3000000001L, copy.ChangeSequence);
            if (state == EmbeddingPreparationState.Obsolete)
            {
                Assert.NotEqual(copy.CurrentPhotoId, restored.PhotoId);
                Assert.Equal(2, restored.PhotoRevision);
            }
            if (state == EmbeddingPreparationState.Ready)
            {
                ImageEmbedding cached = Assert.Single(loaded.RootElements.OfType<ImageEmbedding>());
                Assert.Same(copy, cached.SearchListing);
                Assert.Equal(restored.PhotoId, cached.PhotoId);
                Assert.Equal(restored.ModelVersion, cached.ModelVersion);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("nextAttemptAt")]
    [InlineData("leaseId")]
    [InlineData("leaseExpiresAt")]
    [InlineData("lastError")]
    public void OptionalTaskMetadataCanBeClearedThroughObservableProperties(string name)
    {
        InspectableEmbeddingPreparationTask task = new();
        IAttribute attribute = task.GetClass().Attributes.Single(attribute => attribute.Name == name);
        INotifyExpression<object> expression = task.AttributeExpression(name.ToUpperInvariant());
        expression.Successors.SetDummy();
        try
        {
            Assert.Null(expression.Value);
            object value = AttributeValue(name, false);
            task.SetAttributeValue(attribute, value);
            Assert.Equal(value, expression.Value);
            if (expression is INotifyReversableExpression<string> text)
            {
                text.Value = null!;
            }
            else
            {
                INotifyReversableExpression<object> reversible = Assert.IsType<INotifyReversableExpression<object>>(expression, exactMatch: false);
                reversible.Value = null!;
            }
            Assert.Null(task.GetAttributeValue(attribute));
            Assert.Null(expression.Value);
        }
        finally
        {
            expression.Successors.UnsetAll();
        }
    }

    [Fact]
    public void TaskReferenceExpressionsAndCollectionsPreserveAnIndependentProjection()
    {
        InspectableEmbeddingPreparationTask task = new();
        SearchListing first = new();
        SearchListing second = new();
        IReference reference = Assert.Single(task.GetClass().References);
        INotifyExpression<IModelElement> expression = task.ReferenceExpression("SEARCHLISTING");
        expression.Successors.SetDummy();
        try
        {
            Assert.Null(expression.Value);
            task.SetReferencedElement(reference, first);
            Assert.Same(first, expression.Value);
            INotifyReversableExpression<ISearchListing> reverse = Assert.IsType<INotifyReversableExpression<ISearchListing>>(expression, exactMatch: false);
            reverse.Value = second;
            Assert.Same(second, task.GetReferencedElement(reference));
            task.SearchListing = null!;
            Assert.Null(expression.Value);
        }
        finally
        {
            expression.Successors.UnsetAll();
        }

        EmbeddingPreparationTask.EmbeddingPreparationTaskReferencedElementsCollection items = new(task);
        INotifyCollection<IModelElement> observable = items.AsNotifiable();
        int changes = 0;
        observable.CollectionChanged += (_, _) => changes++;
        observable.Successors.SetDummy();
        try
        {
            Assert.Empty(items);
            Assert.Empty(items);
            ImageEmbedding unrelated = new();
            items.Add(unrelated);
            Assert.Empty(items);
            Assert.DoesNotContain(unrelated, items);
            Assert.False(items.Remove(unrelated));
            items.Add(first);
            Assert.Same(first, task.SearchListing);
            Assert.Contains(first, items);
            Assert.Contains(first, task.ReferencedElements);
            Assert.Empty(task.Children);
            var copy = new IModelElement[2];
            items.CopyTo(copy, 1);
            Assert.Null(copy[0]);
            Assert.Same(first, copy[1]);
            Assert.Same(first, Assert.Single(items.ToArray()));
            Assert.True(items.Remove(first));
            Assert.Null(task.SearchListing);
            items.Add(second);
            items.Clear();
            Assert.Empty(items);
            Assert.Null(task.SearchListing);
            Assert.True(changes > 0);
        }
        finally
        {
            observable.Successors.UnsetAll();
        }

        Model model = new();
        first.BubbledChange += (_, _) => { };
        model.RootElements.Add(first);
        model.RootElements.Add(task);
        task.SearchListing = first;
        first.Delete();
        Assert.Null(task.SearchListing);
        Assert.Contains(task, model.RootElements);
    }

    private sealed class InspectableEmbeddingPreparationTask : EmbeddingPreparationTask, IModelInspection
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

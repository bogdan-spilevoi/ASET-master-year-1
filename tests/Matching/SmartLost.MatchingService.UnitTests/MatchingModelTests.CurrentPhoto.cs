using NMF.Expressions;
using NMF.Models;
using NMF.Models.Meta;
using NMF.Models.Repository;
using SmartLost.MatchingService.Modeling.Matching;
using Xunit;

namespace SmartLost.MatchingService.UnitTests;

public sealed partial class MatchingModelTests
{
    [Theory]
    [InlineData("removed")]
    [InlineData("replaced")]
    [InlineData("revised")]
    public void CurrentPhotoRoundTripRetainsHistoricalEmbeddingsWithoutSelectingThem(string change)
    {
        SearchListing listing = new()
        {
            ListingId = Guid.NewGuid(),
            AuthorId = Guid.NewGuid(),
            CurrentPhotoId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            CurrentPhotoRevision = 2,
            Location = "Campus library",
            OccurredOn = new DateTime(2026, 10, 1)
        };
        ImageEmbedding original = CreateEmbedding(listing, "model-v1");
        Assert.Equal(listing.CurrentPhotoId, original.PhotoId);
        Assert.Equal(listing.CurrentPhotoRevision, original.PhotoRevision);
        switch (change)
        {
            case "removed":
                listing.CurrentPhotoId = null;
                listing.CurrentPhotoRevision = null;
                break;
            case "replaced":
                listing.CurrentPhotoId = Guid.NewGuid();
                listing.CurrentPhotoRevision = 1;
                break;
            case "revised":
                listing.CurrentPhotoRevision = 3;
                break;
        }

        Model model = new();
        model.RootElements.Add(listing);
        model.RootElements.Add(original);
        string path = Path.Combine(Path.GetTempPath(), $"smartlost-current-photo-{Guid.NewGuid():N}.xmi");
        try
        {
            new ModelRepository().Save(model, path);
            Model loaded = new ModelRepository().Resolve(path);
            SearchListing copy = Assert.Single(loaded.RootElements.OfType<SearchListing>());
            ImageEmbedding cached = Assert.Single(loaded.RootElements.OfType<ImageEmbedding>());
            Assert.Equal(listing.CurrentPhotoId, copy.CurrentPhotoId);
            Assert.Equal(listing.CurrentPhotoRevision, copy.CurrentPhotoRevision);
            Assert.Same(copy, cached.SearchListing);
            Assert.Equal(original.PhotoId, cached.PhotoId);
            Assert.Equal(original.PhotoRevision, cached.PhotoRevision);
            Assert.Equal(_components, cached.Vector);
            // The data exposes this mismatch; a runtime selector must enforce it later.
            Assert.False(cached.PhotoId == copy.CurrentPhotoId && cached.PhotoRevision == copy.CurrentPhotoRevision);
            if (change == "removed")
            {
                Assert.Null(copy.CurrentPhotoId);
                Assert.Null(copy.CurrentPhotoRevision);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("currentPhotoId")]
    [InlineData("currentPhotoRevision")]
    public void OptionalCurrentPhotoAttributesSupportReflectiveAndObservableClearing(string name)
    {
        InspectableSearchListing listing = new();
        IAttribute attribute = listing.GetClass().Attributes.Single(attribute => attribute.Name == name);
        INotifyExpression<object> expression = listing.AttributeExpression(name.ToUpperInvariant());
        expression.Successors.SetDummy();
        try
        {
            Assert.Null(expression.Value);
            object value = AttributeValue(name, false);
            listing.SetAttributeValue(attribute, value);
            Assert.Equal(value, expression.Value);
            INotifyReversableExpression<object> reversible = Assert.IsType<INotifyReversableExpression<object>>(expression, exactMatch: false);
            reversible.Value = null!;
            Assert.Null(listing.GetAttributeValue(attribute));
            Assert.Null(expression.Value);
        }
        finally
        {
            expression.Successors.UnsetAll();
        }
    }
}

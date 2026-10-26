using NMF.Models;
using NMF.Models.Meta;
using NMF.Models.Repository;
using SmartLost.ListingsService.Modeling.Listings;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed partial class ListingsModelTests
{
    [Fact]
    public void ChangeSequenceRoundTripsAsALongSeparatelyFromTheContentRevision()
    {
        ModelRepository repository = new();
        Model model = repository.Resolve(Path.Combine(AppContext.BaseDirectory, "Models", "campus-backpacks.xmi"));
        Listing listing = model.RootElements.OfType<Listing>().First();
        int originalRevision = listing.Revision;
        IAttribute sequence = listing.GetClass().Attributes.Single(attribute => attribute.Name == "changeSequence");
        Assert.Equal(1, sequence.LowerBound);
        Assert.Equal(1, sequence.UpperBound);
        listing.ChangeSequence = 3000000001L;
        Assert.Equal(originalRevision, listing.Revision);

        Model copy = RoundTripRecoveryModel(repository, model);
        Listing restored = Assert.Single(copy.RootElements.OfType<Listing>(), item => item.Id == listing.Id);
        Assert.Equal(3000000001L, restored.ChangeSequence);
        Assert.Equal(originalRevision, restored.Revision);
    }
}

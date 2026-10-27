using NMF.Expressions;
using NMF.Models;
using NMF.Models.Collections;
using NMF.Models.Meta;
using NMF.Models.Repository;
using SmartLost.ListingsService.Modeling.Listings;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed partial class ListingsModelTests
{
    [Fact]
    public void DecisionRecoveryAndCredibilityMetadataPreserveIndependentRecords()
    {
        MatchDecision decision = new();
        Recovery recovery = new();
        CredibilityEntry entry = new();
        Assert.Same(MatchDecision.ClassInstance, decision.GetClass());
        Assert.Same(Recovery.ClassInstance, recovery.GetClass());
        Assert.Same(CredibilityEntry.ClassInstance, entry.GetClass());
        Assert.Equal(MatchDecisionState.Suggested, decision.State);
        Assert.Equal(1, decision.LostRevision);
        Assert.Equal(1, decision.FoundRevision);
        Assert.Null(decision.DecidedBy);
        Assert.Null(decision.DecidedAt);
        Assert.Null(decision.Recovery);
        Assert.Equal(1, entry.Points);
        Assert.Equal(5, Enum.GetValues<MatchDecisionState>().Length);

        AssertIndependentReference(decision, "lostListing", "Listing", 1);
        AssertIndependentReference(decision, "foundListing", "Listing", 1);
        AssertIndependentReference(decision, "recovery", "Recovery", 0, "matchDecision");
        AssertIndependentReference(recovery, "matchDecision", "MatchDecision", 1, "recovery");
        AssertIndependentReference(recovery, "credibilityEntry", "CredibilityEntry", 1, "recovery");
        AssertIndependentReference(entry, "recovery", "Recovery", 1, "credibilityEntry");
        Assert.All(new ModelElement[] { decision, recovery, entry }, element => Assert.Empty(element.Children));
    }

    [Fact]
    public void SuggestedDecisionRoundTripPreservesAnAbsentHumanDecision()
    {
        ModelRepository repository = new();
        Model model = repository.Resolve(Path.Combine(AppContext.BaseDirectory, "Models", "campus-backpacks.xmi"));
        Listing lost = Assert.Single(model.RootElements.OfType<Listing>(), listing => listing.Type == ListingType.Lost);
        Listing found = Assert.Single(model.RootElements.OfType<Listing>(), listing => listing.Type == ListingType.Found);
        model.RootElements.Add(new MatchDecision
        {
            Id = Guid.NewGuid(),
            LostListing = lost,
            FoundListing = found,
            LostRevision = lost.Revision,
            FoundRevision = found.Revision
        });

        Model copy = RoundTripRecoveryModel(repository, model);
        MatchDecision decision = Assert.Single(copy.RootElements.OfType<MatchDecision>());
        Assert.Equal(MatchDecisionState.Suggested, decision.State);
        Assert.Null(decision.DecidedBy);
        Assert.Null(decision.DecidedAt);
        Assert.Null(decision.Recovery);
        Assert.Same(copy, decision.Model);
        Assert.Same(copy.RootElements.OfType<Listing>().Single(listing => listing.Type == ListingType.Lost), decision.LostListing);
        Assert.Same(copy.RootElements.OfType<Listing>().Single(listing => listing.Type == ListingType.Found), decision.FoundListing);
        Assert.Equal(ListingState.Open, decision.LostListing.State);
        Assert.Equal(ListingState.Open, decision.FoundListing.State);
        Assert.Empty(copy.RootElements.OfType<Recovery>());
        Assert.Empty(copy.RootElements.OfType<CredibilityEntry>());
    }

    [Fact]
    public void ConfirmedRecoverySamplePreservesAuditLinksAndSharedListings()
    {
        ModelRepository repository = new();
        Model model = repository.Resolve(Path.Combine(AppContext.BaseDirectory, "Models", "matched-recovery.xmi"));
        AssertRecoverySample(model);
        AssertRecoverySample(RoundTripRecoveryModel(repository, model));
    }

    [Fact]
    public void RecoveryAndAwardReferencesMaintainTheirOneToOneOpposites()
    {
        MatchDecision firstDecision = new();
        MatchDecision secondDecision = new();
        Recovery firstRecovery = new()
        {
            MatchDecision = firstDecision
        };
        Recovery secondRecovery = new();
        CredibilityEntry firstEntry = new()
        {
            Recovery = firstRecovery
        };
        CredibilityEntry secondEntry = new();
        Assert.Same(firstRecovery, firstDecision.Recovery);
        Assert.Same(firstEntry, firstRecovery.CredibilityEntry);

        firstRecovery.MatchDecision = secondDecision;
        Assert.Null(firstDecision.Recovery);
        Assert.Same(firstRecovery, secondDecision.Recovery);
        secondDecision.Recovery = secondRecovery;
        Assert.Null(firstRecovery.MatchDecision);
        Assert.Same(secondDecision, secondRecovery.MatchDecision);

        firstEntry.Recovery = secondRecovery;
        Assert.Null(firstRecovery.CredibilityEntry);
        Assert.Same(firstEntry, secondRecovery.CredibilityEntry);
        secondRecovery.CredibilityEntry = secondEntry;
        Assert.Null(firstEntry.Recovery);
        Assert.Same(secondRecovery, secondEntry.Recovery);
        secondEntry.Recovery = null!;
        secondRecovery.MatchDecision = null!;
        Assert.Null(secondRecovery.CredibilityEntry);
        Assert.Null(secondDecision.Recovery);
    }

    [Fact]
    public void RecoveryReferenceExpressionsExposeExplicitListingRolesAndAuditLinks()
    {
        InspectableMatchDecision decision = new();
        AssertReference<IListing>(decision, "lostListing", new Listing(), new Listing());
        AssertReference<IListing>(decision, "foundListing", new Listing(), new Listing());
        AssertReference<IRecovery>(decision, "recovery", new Recovery(), new Recovery());
        InspectableRecovery recovery = new();
        AssertReference<IMatchDecision>(recovery, "matchDecision", new MatchDecision(), new MatchDecision());
        AssertReference<ICredibilityEntry>(recovery, "credibilityEntry", new CredibilityEntry(), new CredibilityEntry());
        InspectableCredibilityEntry entry = new();
        AssertReference<IRecovery>(entry, "recovery", new Recovery(), new Recovery());
    }

    [Theory]
    [InlineData("MatchDecision")]
    [InlineData("Recovery")]
    [InlineData("CredibilityEntry")]
    public void RecoveryReferenceAdaptersEnumerateObserveRemoveAndClearReferences(string className)
    {
        ModelElement element = CreateInspectable(className);
        ReferenceCollection items = element switch
        {
            MatchDecision decision => new MatchDecision.MatchDecisionReferencedElementsCollection(decision),
            Recovery recovery => new Recovery.RecoveryReferencedElementsCollection(recovery),
            CredibilityEntry entry => new CredibilityEntry.CredibilityEntryReferencedElementsCollection(entry),
            _ => throw new ArgumentException("Unsupported recovery model.", nameof(className))
        };
        INotifyCollection<IModelElement> observable = items.AsNotifiable();
        int changes = 0;
        observable.CollectionChanged += (_, _) => changes++;
        observable.Successors.SetDummy();
        try
        {
            Assert.Empty(items);
            Assert.Empty(items);
            Photo unrelated = new();
            items.Add(unrelated);
            Assert.Empty(items);
            Assert.DoesNotContain(unrelated, items);
            Assert.False(items.Remove(unrelated));

            IReference[] references = [.. element.GetClass().References];
            ModelElement[] values = [.. references.Select(reference => CreateRecoveryReference(reference.ReferenceType.Name))];
            foreach (ModelElement value in values)
            {
                items.Add(value);
            }

            for (int index = 0; index < references.Length; index++)
            {
                // Collection Add cannot distinguish LostListing from FoundListing; set roles explicitly.
                element.SetReferencedElement(references[index], values[index]);
            }

            Assert.Equal(references.Length, items.Count);
            Assert.All(values, value => Assert.Contains(value, items));
            Assert.All(values, value => Assert.Contains(value, element.ReferencedElements));
            Assert.Empty(element.Children);
            var copy = new IModelElement[items.Count + 1];
            items.CopyTo(copy, 1);
            Assert.Null(copy[0]);
            Assert.Equal(values, copy.Skip(1));
            Assert.Equal(values, items.ToArray());
            foreach (ModelElement value in values)
            {
                Assert.True(items.Remove(value));
                Assert.DoesNotContain(value, items);
            }

            Assert.Empty(items);
            for (int index = 0; index < references.Length; index++)
            {
                element.SetReferencedElement(references[index], values[index]);
            }

            items.Clear();
            Assert.Empty(items);
            Assert.All(references, reference => Assert.Null(element.GetReferencedElement(reference)));
            Assert.True(changes > 0);
        }
        finally
        {
            observable.Successors.UnsetAll();
        }
    }

    [Theory]
    [InlineData("MatchDecision")]
    [InlineData("Recovery")]
    [InlineData("CredibilityEntry")]
    public void RemovingReferencedModelRootsClearsLinksWithoutContainingTheRecords(string className)
    {
        ModelElement element = CreateInspectable(className);
        element.BubbledChange += (_, _) => { };
        Model model = new();
        model.RootElements.Add(element);
        foreach (IReference reference in element.GetClass().References)
        {
            ModelElement value = CreateRecoveryReference(reference.ReferenceType.Name);
            value.BubbledChange += (_, _) => { };
            model.RootElements.Add(value);
            element.SetReferencedElement(reference, value);
            Assert.NotSame(element, value.Parent);
            Assert.Contains(value, model.RootElements);
            value.Delete();
            Assert.Null(element.GetReferencedElement(reference));
            Assert.Contains(element, model.RootElements);
        }
    }

    private static void AssertIndependentReference(ModelElement element, string name, string target, int lowerBound, string? opposite = null)
    {
        IReference reference = element.GetClass().References.Single(reference => reference.Name == name);
        Assert.False(reference.IsContainment);
        Assert.Equal(lowerBound, reference.LowerBound);
        Assert.Equal(1, reference.UpperBound);
        Assert.Equal(target, reference.ReferenceType.Name);
        Assert.Equal(opposite, reference.Opposite?.Name);
    }

    private static Model RoundTripRecoveryModel(ModelRepository repository, Model model)
    {
        string path = Path.Combine(Path.GetTempPath(), $"smartlost-recovery-{Guid.NewGuid():N}.xmi");
        try
        {
            repository.Save(model, path);
            return new ModelRepository().Resolve(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertRecoverySample(Model model)
    {
        Assert.Equal(7, model.RootElements.Count);
        MatchDecision decision = Assert.Single(model.RootElements.OfType<MatchDecision>());
        Recovery recovery = Assert.Single(model.RootElements.OfType<Recovery>());
        CredibilityEntry entry = Assert.Single(model.RootElements.OfType<CredibilityEntry>());
        Listing lost = Assert.Single(model.RootElements.OfType<Listing>(), listing => listing.Type == ListingType.Lost);
        Listing found = Assert.Single(model.RootElements.OfType<Listing>(), listing => listing.Type == ListingType.Found);
        Assert.Same(lost, decision.LostListing);
        Assert.Same(found, decision.FoundListing);
        Assert.Equal(MatchDecisionState.Confirmed, decision.State);
        Assert.Equal(lost.Revision, decision.LostRevision);
        Assert.Equal(found.Revision, decision.FoundRevision);
        Assert.Equal(3L, lost.ChangeSequence);
        Assert.Equal(3L, found.ChangeSequence);
        Assert.Equal(lost.AuthorId, decision.DecidedBy);
        Assert.NotNull(decision.DecidedAt);
        Assert.Same(recovery, decision.Recovery);
        Assert.Same(decision, recovery.MatchDecision);
        Assert.Equal(lost.AuthorId, recovery.ConfirmedBy);
        Assert.Same(entry, recovery.CredibilityEntry);
        Assert.Same(recovery, entry.Recovery);
        Assert.Equal(found.AuthorId, entry.BeneficiaryId);
        Assert.Equal(lost.AuthorId, entry.HelpedUserId);
        Assert.NotEqual(entry.BeneficiaryId, entry.HelpedUserId);
        Assert.Equal(1, entry.Points);
        Assert.True(decision.DecidedAt <= recovery.ConfirmedAt);
        Assert.Equal(recovery.ConfirmedAt, entry.AwardedAt);
        Assert.Equal(ListingState.Closed, lost.State);
        Assert.Equal(ClosureReason.Recovered, lost.ClosureReason);
        Assert.Equal(ListingState.Open, found.State);
        Assert.All(new ModelElement[] { decision, recovery, entry }, element =>
        {
            Assert.Contains(element, model.RootElements);
            Assert.Empty(element.Children);
            Assert.Same(model, element.Model);
        });
    }

    private static ModelElement CreateRecoveryReference(string className)
    {
        return className switch
        {
            "Listing" => new Listing(),
            "MatchDecision" => new MatchDecision(),
            "Recovery" => new Recovery(),
            "CredibilityEntry" => new CredibilityEntry(),
            _ => throw new ArgumentException("Unsupported recovery reference.", nameof(className))
        };
    }

    private sealed class InspectableMatchDecision : MatchDecision, IModelInspection
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

    private sealed class InspectableRecovery : Recovery, IModelInspection
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

    private sealed class InspectableCredibilityEntry : CredibilityEntry, IModelInspection
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

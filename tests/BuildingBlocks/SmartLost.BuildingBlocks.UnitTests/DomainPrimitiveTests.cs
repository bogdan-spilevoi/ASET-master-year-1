using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.BuildingBlocks.Core.Validation;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class DomainPrimitiveTests
{
    [Fact]
    public void AggregateProtectsItsEventCollectionAndRejectsInvalidEvents()
    {
        var aggregate = new SampleAggregate(Guid.NewGuid());
        var item = new SampleEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);
        aggregate.Raise(item);
        Assert.Equal(item, Assert.Single(aggregate.DomainEvents));
        Assert.Throws<NotSupportedException>(() => ((ICollection<IDomainEvent>)aggregate.DomainEvents).Clear());
        Assert.Throws<ArgumentNullException>(() => aggregate.Raise(null!));
        Assert.Throws<ArgumentException>(() => aggregate.Raise(item));
        Assert.Throws<ArgumentException>(() => aggregate.Raise(item with { Id = Guid.Empty }));
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
        Assert.Equal(Guid.Empty, new SampleAggregate().Id);
    }

    [Fact]
    public void GuardsPreserveBusinessErrorsAndRejectBadConfiguration()
    {
        Error error = new("sample.invalid", "Invalid value.", ErrorKind.Validation);
        Guard.NotNull(new object(), error);
        Guard.NotNullOrWhiteSpace("valid", error, 5);
        Guard.InRange(2, 1, 3, error);
        Guard.Positive(1, error);
        Guard.GreaterThan(2, 1, error);
        Guard.NotNegative(0, error);
        Guard.Against(false, error);
        Action[] invalid = [
            () => Guard.NotNull(null, error),
            () => Guard.NotNullOrWhiteSpace(" ", error),
            () => Guard.NotNullOrWhiteSpace("long", error, 2),
            () => Guard.InRange(0, 1, 3, error),
            () => Guard.Positive(0, error),
            () => Guard.GreaterThan(1, 1, error),
            () => Guard.NotNegative(-1, error),
            () => Guard.Against(true, error)
        ];
        foreach (Action action in invalid)
        {
            Assert.Equal(error, Assert.Throws<DomainException>(action).Error);
        }

        Assert.Throws<ArgumentNullException>(() => Guard.Against(false, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.NotNullOrWhiteSpace("valid", error, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(1, 3, 1, error));
    }

    private sealed record SampleEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class SampleAggregate : AggregateRoot<Guid>
    {
        public SampleAggregate()
        {
        }

        public SampleAggregate(Guid id) : base(id)
        {
        }

        public void Raise(IDomainEvent domainEvent)
        {
            AddDomainEvent(domainEvent);
        }
    }
}

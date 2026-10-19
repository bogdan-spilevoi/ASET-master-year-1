using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class CoreTests
{
    [Fact]
    public void ResultsPreserveSuccessValuesAndRejectInvalidFailures()
    {
        var success = Result.Success();
        var value = Result<string>.Success("value");
        Assert.True(success.IsSuccess);
        Assert.False(success.IsFailure);
        Assert.Empty(success.Errors);
        Assert.True(value.IsSuccess);
        Assert.False(value.IsFailure);
        Assert.Equal("value", value.Value);
        Assert.Empty(value.Errors);

        Error error = new("test.conflict", "Already exists.", ErrorKind.Conflict, "Name");
        Error[] source = [error];
        var failure = Result.Failure(source);
        var genericFailure = Result<string>.Failure(source);
        source[0] = new Error("changed", "Changed.", ErrorKind.Failure);
        Assert.True(failure.IsFailure);
        Assert.False(failure.IsSuccess);
        Assert.True(genericFailure.IsFailure);
        Assert.False(genericFailure.IsSuccess);
        Assert.Equal(error, Assert.Single(failure.Errors));
        Assert.Equal(error, Assert.Single(genericFailure.Errors));
        Assert.Equal("Name", error.PropertyName);
        Assert.Throws<InvalidOperationException>(() => genericFailure.Value);
        Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Failure((Error)null!));
        Assert.Throws<ArgumentNullException>(() => Result<string>.Failure((Error)null!));
        Assert.Throws<ArgumentNullException>(() => Result.Failure((IReadOnlyList<Error>)null!));
        Assert.Throws<ArgumentException>(() => Result.Failure([]));
        Assert.Throws<ArgumentException>(() => Result<string>.Failure([null!]));
        Assert.Single(Result.Failure(error).Errors);
        Assert.Single(Result<string>.Failure(error).Errors);
    }

    [Fact]
    public void EntityEqualityUsesTypeAndAssignedIdentity()
    {
        var id = Guid.NewGuid();
        Entity<Guid> first = new TestEntity(id);
        Entity<Guid> same = new TestEntity(id);
        Entity<Guid> otherType = new OtherEntity(id);
        Entity<Guid> transient = new TestEntity();
        Entity<Guid> otherTransient = new TestEntity();
        Entity<Guid>? missing = null;

        Assert.True(first == same);
        Assert.False(first != same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.True(first.Equals((object)same));
        Assert.False(first.Equals(new object()));
        Assert.False(first == otherType);
        Assert.False(first == new TestEntity(Guid.NewGuid()));
        Assert.False(transient == otherTransient);
        Assert.True(transient.Equals(transient));
        Assert.Equal(transient.GetHashCode(), transient.GetHashCode());
        Assert.False(first == missing);
        Assert.False(missing == first);
        Assert.Null(missing);
    }

    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity()
        {
        }

        public TestEntity(Guid id) : base(id)
        {
        }
    }

    private sealed class OtherEntity(Guid id) : Entity<Guid>(id);
}

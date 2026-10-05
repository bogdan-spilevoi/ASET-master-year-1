using System.Runtime.CompilerServices;

namespace SmartLost.BuildingBlocks.Core.Entities;

public abstract class Entity<TId> : IEquatable<Entity<TId>> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity()
    {
    }

    protected Entity(TId id)
    {
        Id = id;
    }

    public bool Equals(Entity<TId>? other)
    {
        return ReferenceEquals(this, other) ||
            (other is not null && GetType() == other.GetType() &&
             !EqualityComparer<TId>.Default.Equals(Id, default!) &&
             EqualityComparer<TId>.Default.Equals(Id, other.Id));
    }

    public override bool Equals(object? obj)
    {
        return obj is Entity<TId> other && Equals(other);
    }

    public override int GetHashCode()
    {
        return EqualityComparer<TId>.Default.Equals(Id, default!)
            ? RuntimeHelpers.GetHashCode(this)
            : HashCode.Combine(GetType(), Id);
    }

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }
}

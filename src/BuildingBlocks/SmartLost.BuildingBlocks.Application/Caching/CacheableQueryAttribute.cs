namespace SmartLost.BuildingBlocks.Application.Caching;

/// <summary>Opts a read-only query into caching immutable successful results.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CacheableQueryAttribute : Attribute
{
    private int _ttlSeconds = 300;

    public CacheableQueryAttribute(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Prefix = prefix;
    }

    public string Prefix
    {
        get;
    }

    public int TtlSeconds
    {
        get => _ttlSeconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _ttlSeconds = value;
        }
    }
}

namespace SmartLost.BuildingBlocks.Application.Profiling;

/// <summary>Holds the current operation per asynchronous flow, including nested profiling scopes.</summary>
public sealed class ProfilingContext : IProfilingContext
{
    private readonly AsyncLocal<OperationProfile?> _current = new();

    public OperationProfile? Current => _current.Value;

    public IDisposable Begin(OperationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        OperationProfile? previous = _current.Value;
        _current.Value = profile;
        return new Scope(this, previous);
    }

    private sealed class Scope(ProfilingContext context, OperationProfile? previous) : IDisposable
    {
        public void Dispose()
        {
            context._current.Value = previous;
        }
    }
}

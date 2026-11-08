namespace SmartLost.MatchingService.Api.Diagnostics;

/// <summary>Thread-safe ring buffer of the most recent Matching log entries; cleared on restart.</summary>
public sealed class RecentLogBuffer
{
    public const int Capacity = 100;

    private readonly Queue<RecentLogEntry> _entries = new(Capacity);
    private readonly Lock _gate = new();

    public void Add(RecentLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (_entries.Count == Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }
    }

    /// <summary>Returns up to <paramref name="count"/> entries, newest first.</summary>
    public IReadOnlyList<RecentLogEntry> GetLatest(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        lock (_gate)
        {
            return [.. _entries.Reverse().Take(count)];
        }
    }
}

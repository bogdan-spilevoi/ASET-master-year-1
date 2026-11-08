namespace SmartLost.MatchingService.Api.Diagnostics;

/// <summary>
/// Captures Information-or-higher logs from SmartLost.MatchingService categories, such as the
/// profiling events 1301-1304. Framework logs are skipped so they do not push profiling entries out.
/// </summary>
public sealed class RecentLogProvider(RecentLogBuffer buffer, TimeProvider timeProvider) : ILoggerProvider
{
    public const string CategoryPrefix = "SmartLost.MatchingService";
    public const string SharedProfilingCategoryPrefix = "SmartLost.BuildingBlocks.Application.Behaviors.ProfilingBehavior";

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return categoryName.StartsWith(CategoryPrefix, StringComparison.Ordinal) ||
            categoryName.StartsWith(SharedProfilingCategoryPrefix, StringComparison.Ordinal)
            ? new BufferLogger(buffer, timeProvider, categoryName)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    public void Dispose()
    {
    }

    private sealed class BufferLogger(RecentLogBuffer buffer, TimeProvider timeProvider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel is >= LogLevel.Information and not LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // The formatter is called without the exception so its message cannot reach the buffer.
            buffer.Add(new RecentLogEntry(timeProvider.GetUtcNow(), logLevel.ToString(), category, eventId.Id, eventId.Name,
                formatter(state, null), exception?.GetType().Name));
        }
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class OutboxDeliveryServiceTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(3600, true)]
    [InlineData(0, false)]
    [InlineData(3601, false)]
    public void PollingIntervalHasFiniteBounds(int seconds, bool valid)
    {
        Assert.Equal(valid, new OutboxDeliveryOptions { PollInterval = TimeSpan.FromSeconds(seconds) }.IsValid());
    }

    [Fact]
    public async Task EmptyBatchDoesNotStopTheHostAndLaterWorkUsesANewScope()
    {
        State state = new();
        RecordingLogger logger = new();
        using ServiceProvider provider = Provider(state);
        using OutboxDeliveryService service = Create(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(2, state.Calls);
        Assert.Equal(2, state.Disposals);
        Assert.Contains("2 messages published", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchFailureRetriesWithoutTerminatingTheWorker(bool cancellationFailure)
    {
        State state = new()
        {
            FailFirst = true,
            CancellationFailure = cancellationFailure
        };
        RecordingLogger logger = new();
        using ServiceProvider provider = Provider(state);
        using OutboxDeliveryService service = Create(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(2, state.Calls);
        Assert.Equal(2, state.Disposals);
        Assert.Contains(logger.Messages, message => message.Contains("batch failed", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("2 messages published", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostCancellationReachesTheActiveBatchAndDisposesItsScope()
    {
        State state = new()
        {
            Block = true
        };
        RecordingLogger logger = new();
        using ServiceProvider provider = Provider(state);
        using OutboxDeliveryService service = Create(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(1, state.Disposals);
        Assert.Empty(logger.Messages);
    }

    private static ServiceProvider Provider(State state)
    {
        ServiceCollection services = new();
        services.AddScoped<IOutboxProcessor>(_ => new Processor(state));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static OutboxDeliveryService Create(ServiceProvider provider, RecordingLogger logger)
    {
        return new(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxDeliveryOptions { PollInterval = TimeSpan.FromSeconds(1) }), TimeProvider.System, logger);
    }

    private sealed class State
    {
        public bool FailFirst
        {
            get; init;
        }
        public bool CancellationFailure
        {
            get; init;
        }
        public bool Block
        {
            get; init;
        }
        public int Calls
        {
            get; set;
        }
        public int Disposals
        {
            get; set;
        }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Processor(State state) : IOutboxProcessor, IAsyncDisposable
    {
        public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            state.Calls++;
            state.Entered.TrySetResult();
            if (state.Block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (state.FailFirst && state.Calls == 1)
            {
                if (state.CancellationFailure)
                {
                    throw new OperationCanceledException("Batch cancelled without host shutdown.");
                }

                throw new InvalidOperationException("Transient batch failure.");
            }

            return state.Calls == 1 ? 0 : 2;
        }

        public ValueTask DisposeAsync()
        {
            state.Disposals++;
            if (state.Calls >= 2)
            {
                state.Completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<OutboxDeliveryService>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception));
        }
    }
}

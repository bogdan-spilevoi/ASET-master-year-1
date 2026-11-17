using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class OutboxInspectionServiceTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1000, 3600, true)]
    [InlineData(0, 10, false)]
    [InlineData(1001, 10, false)]
    [InlineData(20, 0, false)]
    [InlineData(20, 3601, false)]
    public void OptionsBoundTheBatchAndPollingInterval(int batchSize, int seconds, bool valid)
    {
        OutboxInspectionOptions settings = new()
        {
            BatchSize = batchSize,
            PollInterval = TimeSpan.FromSeconds(seconds)
        };
        Assert.Equal(valid, settings.IsValid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedReadRetriesInANewScopeAndLogsOnlyMessageMetadata(bool cancellationFailure)
    {
        State state = new()
        {
            FailFirst = true,
            CancellationFailure = cancellationFailure
        };
        RecordingLogger logger = new();
        ServiceCollection services = new();
        services.AddScoped<IOutboxReader>(_ => new Reader(state));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using OutboxInspectionService service = Create(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(2, state.Reads);
        Assert.Equal(2, state.Disposals);
        Assert.Contains(logger.Messages, message => message.Contains("inspection failed", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("sample.created", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("private-payload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationDuringAReadDisposesItsScopeWithoutAWarning()
    {
        State state = new()
        {
            Block = true
        };
        RecordingLogger logger = new();
        ServiceCollection services = new();
        services.AddScoped<IOutboxReader>(_ => new Reader(state));
        using ServiceProvider provider = services.BuildServiceProvider();
        using OutboxInspectionService service = Create(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(1, state.Disposals);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyInspectionWaitsAndStopsWithoutPublishing()
    {
        State state = new()
        {
            Empty = true
        };
        RecordingLogger logger = new();
        ServiceCollection services = new();
        services.AddScoped<IOutboxReader>(_ => new Reader(state));
        using ServiceProvider provider = services.BuildServiceProvider();
        using OutboxInspectionService service = Create(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Single(logger.Messages);
        Assert.Contains("0 pending", logger.Messages.Single(), StringComparison.Ordinal);
        Assert.Equal(1, state.Disposals);
        Assert.Null(provider.GetService<IOutboxMessagePublisher>());
    }

    private static OutboxInspectionService Create(ServiceProvider provider, RecordingLogger logger)
    {
        return new OutboxInspectionService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxInspectionOptions { PollInterval = TimeSpan.FromSeconds(1) }), TimeProvider.System, logger);
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
        public bool Empty
        {
            get; init;
        }
        public int Reads
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

    private sealed class Reader(State state) : IOutboxReader, IAsyncDisposable
    {
        public async Task<IReadOnlyList<OutboxEnvelope>> ReadPendingAsync(int batchSize, CancellationToken cancellationToken)
        {
            Assert.Equal(20, batchSize);
            state.Reads++;
            state.Entered.TrySetResult();
            if (state.Block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (state.FailFirst && state.Reads == 1)
            {
                if (state.CancellationFailure)
                {
                    throw new OperationCanceledException("Reader cancelled without stopping the host.");
                }

                throw new InvalidOperationException("Temporary database failure.");
            }

            IReadOnlyList<OutboxEnvelope> result = state.Empty ? [] :
                [new OutboxEnvelope(Guid.NewGuid(), "sample.created", 1, "{\"text\":\"private-payload\"}", DateTimeOffset.UtcNow)];
            return result;
        }

        public ValueTask DisposeAsync()
        {
            state.Disposals++;
            if (!state.FailFirst || state.Reads > 1)
            {
                state.Completed.TrySetResult();
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<OutboxInspectionService>
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

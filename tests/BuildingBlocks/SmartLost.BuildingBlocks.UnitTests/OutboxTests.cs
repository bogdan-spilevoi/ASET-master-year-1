using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class OutboxTests
{
    [Fact]
    public void EnvelopeValidatesItsVersionedJsonContract()
    {
        var id = Guid.NewGuid();
        DateTimeOffset time = DateTimeOffset.Now;
        OutboxEnvelope envelope = new(id, "sample.created", 1, "{}", time);
        Assert.Equal(TimeSpan.Zero, envelope.OccurredOn.Offset);
        Assert.Equal(envelope, OutboxMessage.Create(envelope).ToEnvelope());
        Assert.Throws<ArgumentNullException>(() => OutboxMessage.Create(null!));
        Assert.Throws<ArgumentException>(() => new OutboxEnvelope(Guid.Empty, "sample", 1, "{}", time));
        Assert.Throws<ArgumentException>(() => new OutboxEnvelope(id, " ", 1, "{}", time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxEnvelope(id, new string('x', 201), 1, "{}", time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxEnvelope(id, "sample", 0, "{}", time));
        Assert.Throws<ArgumentException>(() => new OutboxEnvelope(id, "sample", 1, " ", time));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => new OutboxEnvelope(id, "sample", 1, "invalid", time));
    }

    [Fact]
    public void WriterStagesEventsOnceWithoutSavingOrClearingThem()
    {
        using Store store = new();
        var aggregate = new SampleAggregate(Guid.NewGuid());
        var domainEvent = new SampleEvent(Guid.NewGuid(), store.Clock.GetUtcNow());
        aggregate.Raise(domainEvent);
        store.Context.Add(aggregate);
        Mapper mapper = new(item => new OutboxEnvelope(item.Id, "sample.created", 1, "{}", item.OccurredOn));
        OutboxWriter<TestContext> writer = new(store.Context, mapper);
        Assert.Equal(1, writer.EnqueueDomainEvents());
        Assert.Equal(1, writer.EnqueueDomainEvents());
        Assert.Single(store.Context.Set<OutboxMessage>().Local);
        Assert.Single(aggregate.DomainEvents);
        Assert.Empty(store.Context.Set<OutboxMessage>().AsNoTracking().ToArray());
        OutboxEnvelope conflicting = new(domainEvent.Id, "sample.created", 1, "{\"changed\":true}", domainEvent.OccurredOn);
        Assert.Throws<InvalidOperationException>(() => writer.Enqueue(conflicting));
        Assert.Throws<ArgumentNullException>(() => writer.Enqueue(null!));
        Assert.Equal(0, new OutboxWriter<TestContext>(store.Context, new Mapper(_ => null)).EnqueueDomainEvents());
        Mapper wrongId = new(item => new OutboxEnvelope(Guid.NewGuid(), "sample", 1, "{}", item.OccurredOn));
        Assert.Throws<InvalidOperationException>(() => new OutboxWriter<TestContext>(store.Context, wrongId).EnqueueDomainEvents());
    }

    [Fact]
    public async Task FailedDeliveryBacksOffAndCanLaterBeAcknowledged()
    {
        using Store store = new();
        Guid id = store.Enqueue();
        Publisher failure = new((_, _) => Task.FromException(new InvalidOperationException("private transport detail")));
        Assert.Equal(0, await store.Processor(failure).ProcessBatchAsync(CancellationToken.None));
        OutboxMessage failed = await store.ReadAsync(id);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal("outbox.delivery_failed", failed.LastError);
        Assert.Equal(store.Clock.GetUtcNow().AddSeconds(2), failed.NextAttemptAt);
        Assert.Null(failed.LeaseId);
        Publisher success = new((_, _) => Task.CompletedTask);
        Assert.Equal(0, await store.Processor(success).ProcessBatchAsync(CancellationToken.None));
        store.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await store.Processor(success).ProcessBatchAsync(CancellationToken.None));
        OutboxMessage delivered = await store.ReadAsync(id);
        Assert.Equal(2, delivered.AttemptCount);
        Assert.Equal(store.Clock.GetUtcNow(), delivered.ProcessedOn);
        Assert.Null(delivered.LastError);
        Assert.Null(delivered.NextAttemptAt);
        Assert.Null(delivered.LeaseExpiresAt);
    }

    [Fact]
    public async Task FinalFailuresAndExpiredFinalClaimsBecomeDeadLetters()
    {
        using Store store = new();
        Guid failedId = store.Enqueue();
        Publisher failure = new((_, _) => Task.FromException(new InvalidOperationException()));
        Assert.Equal(0, await store.Processor(failure, new OutboxOptions { MaximumAttempts = 1 }).ProcessBatchAsync(CancellationToken.None));
        Assert.NotNull((await store.ReadAsync(failedId)).DeadLetteredAt);
        Guid crashedId = store.Enqueue();
        OutboxMessage tracked = store.Context.Set<OutboxMessage>().Local.Single(message => message.Id == crashedId);
        store.Context.Entry(tracked).Property(message => message.AttemptCount).CurrentValue = 1;
        store.Context.Entry(tracked).Property(message => message.LeaseId).CurrentValue = Guid.NewGuid();
        store.Context.Entry(tracked).Property(message => message.LeaseExpiresAt).CurrentValue = store.Clock.GetUtcNow().AddSeconds(-1);
        await store.Context.SaveChangesAsync();
        Assert.Equal(0, await store.Processor(failure, new OutboxOptions { MaximumAttempts = 1 }).ProcessBatchAsync(CancellationToken.None));
        OutboxMessage expired = await store.ReadAsync(crashedId);
        Assert.Equal("outbox.attempt_limit_reached", expired.LastError);
        Assert.NotNull(expired.DeadLetteredAt);
        Assert.Null(expired.LeaseId);
    }

    [Fact]
    public async Task CancellationLeavesAnExpiringClaimAndTransactionsAreRejected()
    {
        using Store store = new();
        Guid id = store.Enqueue();
        using CancellationTokenSource cancellation = new();
        Publisher canceled = new((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Processor(canceled).ProcessBatchAsync(cancellation.Token));
        Assert.NotNull((await store.ReadAsync(id)).LeaseId);
        Publisher success = new((_, _) => Task.CompletedTask);
        Assert.Equal(0, await store.Processor(success).ProcessBatchAsync(CancellationToken.None));
        store.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(1, await store.Processor(success).ProcessBatchAsync(CancellationToken.None));
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await store.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Processor(success).ProcessBatchAsync(CancellationToken.None));
    }

    [Fact]
    public void RegistrationValidatesOptionsAndResolvesTheServiceAdapters()
    {
        using Store store = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(store.Context);
        services.AddSingleton<IOutboxEventMapper>(new Mapper(_ => null));
        services.AddSingleton<IOutboxMessagePublisher>(new Publisher((_, _) => Task.CompletedTask));
        services.AddBuildingBlocksOutbox<TestContext>();
        services.AddBuildingBlocksOutboxProcessor<TestContext>(settings => settings.BatchSize = 0);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<OutboxWriter<TestContext>>(scope.ServiceProvider.GetRequiredService<IOutboxWriter>());
        Assert.IsType<OutboxProcessor<TestContext>>(scope.ServiceProvider.GetRequiredService<IOutboxProcessor>());
        Assert.NotNull(provider.GetRequiredService<TimeProvider>());
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OutboxOptions>>().Value);
    }

    private sealed record SampleEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class SampleAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void Raise(IDomainEvent domainEvent)
        {
            AddDomainEvent(domainEvent);
        }
    }

    private sealed class Mapper(Func<IDomainEvent, OutboxEnvelope?> map) : IOutboxEventMapper
    {
        public OutboxEnvelope? Map(IDomainEvent domainEvent)
        {
            return map(domainEvent);
        }
    }

    private sealed class Publisher(Func<OutboxEnvelope, CancellationToken, Task> publish) : IOutboxMessagePublisher
    {
        public Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
        {
            return publish(envelope, cancellationToken);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.Entity<SampleAggregate>().HasKey(entity => entity.Id);
            modelBuilder.Entity<SampleAggregate>().Ignore(entity => entity.DomainEvents);
            // SQLite fixture only: use UTC ticks for SQL ordering/comparison; production stays timestamptz.
            ValueConverter<DateTimeOffset, long> converter = new(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
            foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableProperty property in modelBuilder.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties())
                .Where(property => property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(converter);
            }
        }
    }

    private sealed class Store : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public Store()
        {
            _connection.Open();
            Context = new TestContext(new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options);
            Context.Database.EnsureCreated();
        }

        public TestContext Context
        {
            get;
        }

        public TestClock Clock { get; } = new();

        public Guid Enqueue()
        {
            var envelope = new OutboxEnvelope(Guid.NewGuid(), "sample.created", 1, "{}", Clock.GetUtcNow());
            Context.Add(OutboxMessage.Create(envelope));
            Context.SaveChanges();
            return envelope.Id;
        }

        public Task<OutboxMessage> ReadAsync(Guid id)
        {
            return Context.Set<OutboxMessage>().AsNoTracking().SingleAsync(message => message.Id == id);
        }

        public OutboxProcessor<TestContext> Processor(IOutboxMessagePublisher publisher, OutboxOptions? settings = null)
        {
            return new OutboxProcessor<TestContext>(Context, publisher, Options.Create(settings ?? new OutboxOptions()),
                Clock, NullLogger<OutboxProcessor<TestContext>>.Instance);
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}

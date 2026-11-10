using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests.Transactions;

public sealed class EfUnitOfWorkTests
{
    [Fact]
    public async Task PlainContextsSupportCommitRollbackAndDisposalWithoutOutbox()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using PlainContext context = new(new DbContextOptionsBuilder<PlainContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        var unitOfWork = new EfUnitOfWork<PlainContext>(context);
        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            context.Rows.Add(new PlainRow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            context.Rows.Add(new PlainRow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.Empty(context.ChangeTracker.Entries());
        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            context.Rows.Add(new PlainRow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Rows.CountAsync());
    }

    [Fact]
    public async Task IndependentOutboxContextRetainsEventsUntilCommitWithoutDuplicateMessages()
    {
        using Store store = new();
        EventAggregate aggregate = store.AddAggregate();
        await using ITransaction transaction = await store.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        Assert.True(store.Context.HasManagedTransaction);
        await store.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        store.Context.SaveChanges();
        Assert.Single(aggregate.DomainEvents);
        Assert.Equal(1, await store.Context.Set<OutboxMessage>().CountAsync());

        await transaction.CommitAsync(CancellationToken.None);

        Assert.False(store.Context.HasManagedTransaction);
        Assert.Empty(aggregate.DomainEvents);
        Assert.Equal(1, await store.Context.Aggregates.CountAsync());
    }

    [Fact]
    public async Task ImplicitSavesClearEventsOnlyAfterTheSaveSucceeds()
    {
        using Store store = new();
        EventAggregate first = store.AddAggregate();
        store.Context.SaveChanges();
        Assert.Empty(first.DomainEvents);
        EventAggregate second = store.AddAggregate();
        await store.Context.SaveChangesAsync();
        Assert.Empty(second.DomainEvents);
        Assert.Equal(2, await store.Context.Set<OutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task CommitFailureRetainsEventsAndRollbackRemovesAllFlushedData()
    {
        using Store store = new(new RejectCommit());
        EventAggregate aggregate = store.AddAggregate();
        await using ITransaction transaction = await store.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        await store.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(CancellationToken.None));
        Assert.Single(aggregate.DomainEvents);
        Assert.True(store.Context.HasManagedTransaction);

        await transaction.RollbackAsync(CancellationToken.None);

        Assert.False(store.Context.HasManagedTransaction);
        Assert.Single(aggregate.DomainEvents);
        Assert.Empty(store.Context.ChangeTracker.Entries());
        Assert.Equal(0, await store.Context.Aggregates.CountAsync());
        Assert.Equal(0, await store.Context.Set<OutboxMessage>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisallowedSaveBoundariesRejectBeforeStagingMessages(bool ambient)
    {
        using Store store = new();
        EventAggregate aggregate = store.AddAggregate();
        if (ambient)
        {
            using var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.Context.SaveChangesAsync());
        }
        else
        {
            store.Context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.Context.SaveChangesAsync());
        }

        Assert.Empty(store.Context.Set<OutboxMessage>().Local);
        Assert.Single(aggregate.DomainEvents);
    }

    private sealed class PlainRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
    {
        public DbSet<PlainRow> Rows => Set<PlainRow>();
    }

    private sealed record SampleEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class EventAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void Raise()
        {
            AddDomainEvent(new SampleEvent(Guid.NewGuid(), DateTimeOffset.UtcNow));
        }
    }

    private sealed class Mapper : IOutboxEventMapper
    {
        public OutboxEnvelope? Map(IDomainEvent domainEvent)
        {
            return new OutboxEnvelope(domainEvent.Id, "sample.created", 1, "{}", domainEvent.OccurredOn);
        }
    }

    private sealed class EventContext(DbContextOptions<EventContext> options) : OutboxDbContext(options, new Mapper())
    {
        public DbSet<EventAggregate> Aggregates => Set<EventAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EventAggregate>().HasKey(entity => entity.Id);
            modelBuilder.Entity<EventAggregate>().Ignore(entity => entity.DomainEvents);
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        }
    }

    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated commit failure");
        }
    }

    private sealed class Store : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public EventContext Context
        {
            get;
        }
        public EfUnitOfWork<EventContext> UnitOfWork
        {
            get;
        }

        public Store(IInterceptor? interceptor = null)
        {
            _connection.Open();
            DbContextOptionsBuilder<EventContext> builder = new DbContextOptionsBuilder<EventContext>().UseSqlite(_connection);
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }

            Context = new EventContext(builder.Options);
            Context.Database.EnsureCreated();
            UnitOfWork = new EfUnitOfWork<EventContext>(Context);
        }

        public EventAggregate AddAggregate()
        {
            var aggregate = new EventAggregate(Guid.NewGuid());
            aggregate.Raise();
            Context.Aggregates.Add(aggregate);
            return aggregate;
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}

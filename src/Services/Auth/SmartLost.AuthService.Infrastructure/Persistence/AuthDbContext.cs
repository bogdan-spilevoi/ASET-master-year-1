using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, IOutboxEventMapper? eventMapper = null) : DbContext(options)
{
    private readonly IOutboxEventMapper _eventMapper = eventMapper ?? new AuthOutboxEventMapper();
    private readonly HashSet<IHasDomainEvents> _savedAggregates = [];
    private IDbContextTransaction? _pipelineTransaction;

    internal bool HasPipelineTransaction => _pipelineTransaction is not null;

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IHasDomainEvents[] aggregates = StageDomainEvents();
        int saved = base.SaveChanges(acceptAllChangesOnSuccess);
        OnSaved(aggregates);
        return saved;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IHasDomainEvents[] aggregates = StageDomainEvents();
        int saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        OnSaved(aggregates);
        return saved;
    }

    private IHasDomainEvents[] StageDomainEvents()
    {
        IHasDomainEvents[] aggregates = [.. ChangeTracker.Entries()
            .Select(entry => entry.Entity).OfType<IHasDomainEvents>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)];
        if (aggregates.Length == 0)
        {
            return aggregates;
        }

        // Only the pipeline's transaction can defer event cleanup until its final commit.
        if ((Database.CurrentTransaction is not null && !ReferenceEquals(Database.CurrentTransaction, _pipelineTransaction)) ||
            System.Transactions.Transaction.Current is not null ||
            Database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
        {
            throw new InvalidOperationException("Auth event saves require EF-owned automatic or pipeline-managed transactions.");
        }

        var writer = new OutboxWriter<AuthDbContext>(this, _eventMapper);
        writer.EnqueueDomainEvents();
        return aggregates;
    }

    internal async Task<IDbContextTransaction> BeginPipelineTransactionAsync(CancellationToken cancellationToken)
    {
        if (HasPipelineTransaction || Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Auth pipeline transactions cannot be nested or join an external transaction.");
        }

        _pipelineTransaction = await Database.BeginTransactionAsync(cancellationToken);
        return _pipelineTransaction;
    }

    internal void CompletePipelineTransaction(bool committed)
    {
        if (committed)
        {
            ClearEvents(_savedAggregates);
        }
        else
        {
            // SaveChanges may have accepted changes before the database rollback.
            // Detach those entities so a later operation cannot flush rolled-back work.
            ChangeTracker.Clear();
        }

        _savedAggregates.Clear();
        _pipelineTransaction = null;
    }

    private void OnSaved(IHasDomainEvents[] aggregates)
    {
        if (HasPipelineTransaction)
        {
            _savedAggregates.UnionWith(aggregates);
        }
        else
        {
            ClearEvents(aggregates);
        }
    }

    private static void ClearEvents(IEnumerable<IHasDomainEvents> aggregates)
    {
        foreach (IHasDomainEvents aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }
    }
}

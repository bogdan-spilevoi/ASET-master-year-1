using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxWriter<TContext>(TContext context, IOutboxEventMapper mapper) : IOutboxWriter
    where TContext : DbContext
{
    public void Enqueue(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        OutboxMessage? existing = context.Set<OutboxMessage>().Local.FirstOrDefault(message => message.Id == envelope.Id);
        if (existing is not null)
        {
            if (existing.ToEnvelope() != envelope)
            {
                throw new InvalidOperationException("An outbox ID cannot identify different messages.");
            }

            return;
        }

        context.Set<OutboxMessage>().Add(OutboxMessage.Create(envelope));
    }

    public int EnqueueDomainEvents()
    {
        IDomainEvent[] events = [.. context.ChangeTracker.Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .SelectMany(entity => entity.DomainEvents)];
        int count = 0;
        foreach (IDomainEvent domainEvent in events)
        {
            OutboxEnvelope? envelope = mapper.Map(domainEvent);
            if (envelope is null)
            {
                continue;
            }

            if (envelope.Id != domainEvent.Id)
            {
                throw new InvalidOperationException("The exported message must preserve the domain event ID.");
            }

            Enqueue(envelope);
            count++;
        }

        return count;
    }
}

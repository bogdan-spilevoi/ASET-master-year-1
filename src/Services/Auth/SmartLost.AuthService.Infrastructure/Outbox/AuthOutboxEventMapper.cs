using System.Text.Json;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.AuthService.Domain.Events;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.AuthService.Infrastructure.Outbox;

public sealed class AuthOutboxEventMapper : IOutboxEventMapper
{
    public OutboxEnvelope? Map(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return domainEvent switch
        {
            UserAccountCreated created => CreateEnvelope(created, AuthIntegrationEventTypes.UserCreated,
                new UserProfileSnapshot(created.UserId, created.UserName, created.IdentityVersion)),
            UserIdentityChanged changed => CreateEnvelope(changed, AuthIntegrationEventTypes.UserIdentityChanged,
                new UserProfileSnapshot(changed.UserId, changed.UserName, changed.IdentityVersion)),
            _ => null
        };
    }

    private static OutboxEnvelope CreateEnvelope(IDomainEvent source, string type, UserProfileSnapshot payload)
    {
        return new OutboxEnvelope(source.Id, type, 1,
            JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), source.OccurredOn);
    }
}

using System.Text.Json;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.AuthService.Infrastructure.Outbox;

public sealed class AuthOutboxEmailPublisher(IEmailSender emailSender, AuthEmailComposer composer, TimeProvider clock) : IOutboxMessagePublisher
{
    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.Type, AuthOutboxMessageTypes.ConfirmationEmailRequested, StringComparison.Ordinal) ||
            envelope.Version is not (1 or 2))
        {
            throw new InvalidOperationException("Unsupported Auth email outbox message contract.");
        }

        // Drain messages queued before the v2 producer was deployed.
        if (envelope.Version == 1)
        {
            EmailMessage legacy = Read<EmailMessage>(envelope);
            await emailSender.SendAsync(legacy, cancellationToken);
            return;
        }

        ConfirmationEmailRequested request = Read<ConfirmationEmailRequested>(envelope);
        if (request.ExpiresAtUtc > clock.GetUtcNow())
        {
            EmailMessage message = composer.ComposeConfirmation(envelope.Id, request);
            await emailSender.SendAsync(message, cancellationToken);
        }
    }

    private static T Read<T>(OutboxEnvelope envelope) where T : class
    {
        return JsonSerializer.Deserialize<T>(envelope.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Auth email outbox payload is missing.");
    }
}

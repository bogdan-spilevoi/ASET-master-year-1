using System.Text.Json;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.AuthService.Infrastructure.Outbox;

public sealed class AuthOutboxEmailPublisher(IEmailSender emailSender) : IOutboxMessagePublisher
{
    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.Type, AuthOutboxMessageTypes.ConfirmationEmailRequested, StringComparison.Ordinal) ||
            envelope.Version != 1)
        {
            throw new InvalidOperationException("Unsupported Auth email outbox message contract.");
        }

        EmailMessage message = JsonSerializer.Deserialize<EmailMessage>(envelope.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Auth email outbox payload is missing.");
        await emailSender.SendAsync(message, cancellationToken);
    }
}

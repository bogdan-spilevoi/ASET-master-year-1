using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.AuthService.Infrastructure.Outbox;

public sealed class AuthOutboxHttpPublisher(HttpClient httpClient, IOptions<AuthOutboxDeliveryOptions> options)
    : IOutboxMessagePublisher
{
    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        AuthOutboxDeliveryOptions settings = options.Value;
        if (!settings.Routes.TryGetValue(envelope.Type, out string? endpoint))
        {
            throw new InvalidOperationException("No delivery route is configured for the Auth integration event.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-SmartLost-Service-Key", settings.ServiceKey);
        request.Headers.Add("Idempotency-Key", envelope.Id.ToString());
        request.Content = JsonContent.Create(envelope);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

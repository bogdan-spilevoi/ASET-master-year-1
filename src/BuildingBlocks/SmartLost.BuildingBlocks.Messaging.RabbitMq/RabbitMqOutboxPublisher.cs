using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Messaging.RabbitMq;

/// <summary>Serialized, persistent mandatory publishing; success means broker confirm, not consumer processing.</summary>
public sealed class RabbitMqOutboxPublisher(IConnectionFactory factory, IOptions<RabbitMqOptions> options)
    : IOutboxMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private int _disposed;

    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_connection is not { IsOpen: true } || _channel is not { IsOpen: true })
            {
                await ResetAsync();
                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
                await RabbitMqTopology.DeclareAsync(_channel, options.Value, cancellationToken);
            }

            BasicProperties properties = new()
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = envelope.Id.ToString("D"),
                Type = envelope.Type,
                Timestamp = new AmqpTimestamp(envelope.OccurredOn.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?> { ["schema-version"] = envelope.Version }
            };
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonSerializerOptions.Web);
            await _channel.BasicPublishAsync(options.Value.Exchange, envelope.Type, mandatory: true,
                basicProperties: properties, body: body, cancellationToken: cancellationToken);
        }
        catch
        {
            await ResetAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            await ResetAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task ResetAsync()
    {
        IChannel? channel = _channel;
        IConnection? connection = _connection;
        _channel = null;
        _connection = null;
        try
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }
}

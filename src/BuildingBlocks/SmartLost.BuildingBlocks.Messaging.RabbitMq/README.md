# SmartLost.BuildingBlocks.Messaging.RabbitMq

Opt-in RabbitMQ.Client 7.2.2 implementation of Application's `IOutboxMessagePublisher`.
References Application only; it owns no service context or business schema. API hosts and
Auth/Matching do not register this transport. Listings' Publish worker is the first caller.

## Registration and delivery

Call `services.AddRabbitMqOutboxPublisher(configuration)` alongside the service's shared
Outbox processor/delivery host. The `RabbitMq` section is validated at host startup;
credentials belong in ignored local configuration or Secret-backed environment variables.
See the [Listings settings example](../../Services/Listings/SmartLost.ListingsService.Worker/outboxsettings.example.json).

- Singleton connection/publisher; one semaphore serializes channel use. Connections are
  opened lazily on the first message, with automatic recovery disabled. A failure discards
  the channel/connection; a later Outbox retry reconnects and redeclares topology.
- Durable topic exchange, configured durable classic queues and topic bindings. All bindings
  are required on declaration; incompatible existing topology fails delivery rather than
  silently accepting a different queue contract.
- Persistent JSON envelope, routing key = envelope Type, stable MessageId, occurrence
  timestamp and `schema-version` header. The Web-JSON body contains id/type/version/payload/
  occurredOn; payload retains the existing contract's JSON **string** representation.
- `mandatory=true` plus publisher confirmations and confirmation tracking. Awaited success
  means the broker accepted and routed the message to a queue. A return/nack, disconnect
  or timeout fails the call; the shared processor schedules retry/dead-letter state.

Broker acknowledgement does not mean a consumer applied the event. At-least-once delivery
can duplicate an ID after acknowledgement but before the Outbox update. Consumers must
record/deduplicate IDs and handle obsolete ChangeSequence values; neither this publisher
nor parallel workers guarantees per-aggregate delivery order. There is no consumer here.
A single classic queue/broker with a PVC survives pod replacement, not loss of its disk or
cluster; this local setup does not provide broker high availability.

## Configuration

HostName, Port, VirtualHost, UserName, Password, Exchange, ConnectionName,
ConnectionTimeout (1–60 seconds), UseTls and Bindings are supported. TLS uses the normal
platform trust store and validates the configured hostname; it is disabled only for the
local cluster. Bindings maps queue names to nonempty arrays of topic routing keys.

Unit tests exercise confirms, persistent/mandatory properties, reuse, concurrent calls,
failures/reconnection and configuration. Listings integration tests use isolated real
RabbitMQ/PostgreSQL containers to verify confirms, returns, retries and broker restart.
See the [shared guide](../../../docs/BUILDING_BLOCKS.md) and
[Kubernetes runbook](../../../deploy/kubernetes/README.md).

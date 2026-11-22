# Listings Outbox worker

Standalone .NET 10 host with explicit `Worker:Mode`: **Inspect** (default) or **Publish**.
Kubernetes selects Publish. Root Compose does not start this worker or RabbitMQ.
There is no HTTP listener, migration on startup or Matching consumer.
See [communication diagrams](../COMMUNICATION.md).

## Modes

| Mode | Behavior |
| --- | --- |
| Inspect | Shared no-tracking reader samples the oldest 20 pending rows every 10 seconds; logs metadata only. Never claims, acknowledges or changes attempts. Deferred/leased rows may appear repeatedly. |
| Publish | Shared delivery host polls every 2 seconds using a fresh scope; processor leases due rows and sends up to 20 through the shared RabbitMQ publisher. |

Publish connects lazily and declares durable topic exchange `smartlost.events`, durable
classic queue `smartlost.listings.notifications`, binding `listings.*`. Messages are
persistent, mandatory and awaited with publisher confirms. `ProcessedOn` records broker
acceptance, **not consumer processing**. The queue can accumulate messages without a
consumer. Messages are not automatically deleted from the Outbox after delivery.

Failures release the claim and schedule exponential retries (2 seconds, capped at 5
minutes); default maximum is 8 attempts, then the row is dead-lettered **in PostgreSQL**.
There is no automatic dead-letter replay, broker DLQ or alerting component. Inspect the
row/error and correct the cause before a controlled manual replay. Expired leases can be
reclaimed; host cancellation can leave an in-flight lease until its 2-minute expiry.
A duplicate is possible after broker acknowledgement but before the DB update. Future
consumers need ID deduplication and ChangeSequence handling; ordering is not guaranteed.

No broker availability check occurs when there are no due messages. A Running worker
means the process is alive; review logs and Outbox state to assess actual delivery.
`OutboxDelivery:PollInterval` accepts 1 second–1 hour. See the
[transport configuration](../../../BuildingBlocks/SmartLost.BuildingBlocks.Messaging.RabbitMq/README.md).

## Database and notification contracts

Apply `AddListingsOutbox` through the [migration commands](../SmartLost.ListingsService.Infrastructure/README.md#ef-cli).
Business changes and notifications share the Listings transaction. No additional RabbitMQ
migration is needed. Current version-1 payloads:

| Type | Payload |
| --- | --- |
| `listings.listing-changed` | Listing ID, revision, change sequence and state. |
| `listings.match-decision-changed` | Decision ID, state and optional recovery ID. |

Envelopes preserve event ID/time. These metadata notifications are **not full Matching
snapshots**. Matching integration is still pending; no consumer or cross-service DB access
has been added. Migration/unchanged seed reruns do not invent historical notifications.
No mutation HTTP endpoint is introduced.

## Local host

Create the ignored settings file, edit database credentials, and leave Inspect for
read-only use. The example database uses Compose host port 5433:

```sh
cp -n src/Services/Listings/SmartLost.ListingsService.Worker/outboxsettings.example.json src/Services/Listings/SmartLost.ListingsService.Worker/outboxsettings.json
dotnet run --project src/Services/Listings/SmartLost.ListingsService.Worker -- --SettingsFile "$PWD/src/Services/Listings/SmartLost.ListingsService.Worker/outboxsettings.json"
```

Publish requires real RabbitMq connection/credential/binding settings and
`--Worker:Mode Publish`. The cluster broker has no host AMQP mapping; running the worker
inside Kubernetes is the documented publishing setup. Environment variables override JSON,
command-line settings override both. Settings are excluded from Git/image/publish output.

## Containers and Kubernetes

```sh
docker build --provenance=false --file src/Services/Listings/SmartLost.ListingsService.Worker/Dockerfile --tag smartlost-listings-worker:local .
kubectl --context kind-smartlost-local -n smartlost logs deployment/listings-outbox-worker --tail=50
```

The non-root worker Deployment has one replica, no Service/port, ConfigMap JSON and
Secret-backed database/Rabbit credentials. The [Kubernetes runbook](../../../../deploy/kubernetes/README.md)
covers the broker StatefulSet/PVC, patched broker image, migration ordering and startup.
Existing CI builds/scans the worker image; configured CI is distinct from a successful run.

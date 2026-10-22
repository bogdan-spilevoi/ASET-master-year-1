# Member 6 Strategy and Transactional Outbox

Assigned patterns: **Strategy** and **Transactional Outbox**.

Use this guide to present existing project code. Follow the source links in the
repository checkout or on GitHub. Allow about three minutes to explain the two
patterns and their participants, then trace the existing execution flow.

**Strategy:** show [OutboxProcessor](../../../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Outbox/OutboxProcessor.cs)
depending on [IOutboxMessagePublisher](../../../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Outbox/IOutboxMessagePublisher.cs)
and calling the injected publisher. [AuthOutboxHttpPublisher](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Outbox/AuthOutboxHttpPublisher.cs)
provides the existing HTTP strategy. The processor's lease/retry algorithm does not
know the transport. Only one production transport exists; do not claim a broker strategy.

**Transactional Outbox:** show message staging in
[AuthDbContext](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/AuthDbContext.cs),
the [worker job](../../../src/Services/Auth/SmartLost.AuthService.Worker/AuthOutboxJob.cs) and
the [saved communication diagram](../../../src/Services/Auth/COMMUNICATION.md#durable-profile-notification-and-delivery).
Explain the atomic database commit followed by delivery in a separate finite process.
Strategy identifies how delivery is delegated; Outbox identifies how the notification
survives a failure between saving the business change and sending it.

Trace: user change + message commit -> worker claims row -> HTTP publication -> mark
processed or retain retry/dead-letter state. Mention duplicate delivery, the receiver's
deduplication obligation, and that no receiver is implemented. This pair contains one
GoF pattern and one architectural pattern.

## Presentation preparation

- Locate each linked implementation and identify the contract, caller and collaborating types.
- Explain why the code fits each pattern, including any framework/generated contribution.
- Use the trace above to show the relationships in an existing flow.
- Preserve the implementation limits described above when answering questions.

[All six member guides](../README.md) · [Source-backed pattern inventory](../../DESIGN_PATTERNS.md)

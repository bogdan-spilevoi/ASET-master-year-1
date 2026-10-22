# Member 4 Repository and Unit of Work

Assigned patterns: **Repository** and **Unit of Work**.

Use this guide to present existing project code. Follow the source links in the
repository checkout or on GitHub. Allow about three minutes to explain the two
patterns and their participants, then trace the existing execution flow.

**Repository:** show [IUserAccountRepository](../../../src/Services/Auth/SmartLost.AuthService.Application/Interfaces/IUserAccountRepository.cs)
and [UserAccountRepository](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/UserAccountRepository.cs).
The application requests user lookups/additions through a contract; infrastructure
implements the queries and persistence integration using EF Core.

**Unit of Work:** show [AuthDbContext](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/AuthDbContext.cs)
and [AuthenticationSessionService.CreateAsync](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Authentication/AuthenticationSessionService.cs).
Registration stages a user, session, refresh-token history and selected outbox message
in the same context, then saves them at one transaction boundary. Adding a tracked
entity is not the database commit.

Trace: stage entities -> repository SaveChangesAsync -> context save -> commit.
Explain that the active Unit of Work is EF's context behavior, not the optional shared
transaction abstraction. Both patterns are enterprise patterns rather than GoF.
Hand off to Member 5 for the separate Listings model, then Member 6 returns to delivery.

## Presentation preparation

- Locate each linked implementation and identify the contract, caller and collaborating types.
- Explain why the code fits each pattern, including any framework/generated contribution.
- Use the trace above to show the relationships in an existing flow.
- Preserve the implementation limits described above when answering questions.

[All six member guides](../README.md) · [Source-backed pattern inventory](../../DESIGN_PATTERNS.md)

# Member 3 Adapter and application construction

Assigned patterns: **Adapter** and **Builder**.

Use this guide to present existing project code. Follow the source links in the
repository checkout or on GitHub. Allow about three minutes to explain the two
patterns and their participants, then trace the existing execution flow.

**Adapter:** show [IPasswordService](../../../src/Services/Auth/SmartLost.AuthService.Application/Interfaces/IPasswordService.cs)
as the application target and [PasswordService](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Authentication/PasswordService.cs)
wrapping `IPasswordHasher<UserAccount>`. Trace `Verify` into `VerifyHashedPassword`
and the conversion from the framework result enum into the application's boolean.
The adapter translates contracts; it does not implement a new hashing algorithm.

**Builder:** show [API Program.cs](../../../src/Services/Auth/SmartLost.AuthService.Api/Program.cs)
and [worker Program.cs](../../../src/Services/Auth/SmartLost.AuthService.Worker/Program.cs).
Identify the builder, configuration steps and resulting application/host returned by
`Build()`. This is existing framework usage, not a custom SmartLost builder class.

Trace: create builder -> configure services -> build host; then show how
[Auth registration](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/DependencyInjection.cs)
supplies the password adapter. Distinguish builder configuration from runtime request
handling. Hand off to Member 4 at the persistence abstraction.

## Presentation preparation

- Locate each linked implementation and identify the contract, caller and collaborating types.
- Explain why the code fits each pattern, including any framework/generated contribution.
- Use the trace above to show the relationships in an existing flow.
- Preserve the implementation limits described above when answering questions.

[All six member guides](../README.md) · [Source-backed pattern inventory](../../DESIGN_PATTERNS.md)

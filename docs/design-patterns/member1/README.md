# Member 1 Request dispatch

Assigned patterns: **Command** and **Mediator**.

Use this guide to present existing project code. Follow the source links in the
repository checkout or on GitHub. Allow about three minutes to explain the two
patterns and their participants, then trace the existing execution flow.

**Command:** show [RegisterUserCommand](../../../src/Services/Auth/SmartLost.AuthService.Application/Commands/Register/RegisterUserCommand.cs)
and [RegisterUserCommandHandler](../../../src/Services/Auth/SmartLost.AuthService.Application/Commands/Register/RegisterUserCommandHandler.cs).
The request carries registration data; the handler owns execution of that use case.
Explain the separation between the request object and its handler rather than claiming
the request contains an `Execute` method or supports undo.

**Mediator:** show [AuthController](../../../src/Services/Auth/SmartLost.AuthService.Api/Controllers/AuthController.cs)
calling `ISender.Send` and the [MediatR registration](../../../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/DependencyInjection.cs).
The controller knows the sender contract, while the framework resolves the matching
handler. Contrast encapsulating the requested action with dispatching that request.

Trace: register HTTP request -> mapped command -> sender -> pipeline -> handler.
Hand off to Member 2 at the pipeline boundary. Login and refresh are additional
existing examples, not additional pattern credits.

## Presentation preparation

- Locate each linked implementation and identify the contract, caller and collaborating types.
- Explain why the code fits each pattern, including any framework/generated contribution.
- Use the trace above to show the relationships in an existing flow.
- Preserve the implementation limits described above when answering questions.

[All six member guides](../README.md) · [Source-backed pattern inventory](../../DESIGN_PATTERNS.md)

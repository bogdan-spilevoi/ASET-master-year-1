# Shared Application building blocks

This library owns reusable MediatR exception, validation, optional transaction,
authorization and profiling behaviors, plus framework-independent application contracts.
It depends on Core, MediatR, FluentValidation and DI/logging abstractions; it never
references a service, EF Core or ASP.NET Core.

## Profiling opt-in

`AddBuildingBlocksApplication(assembly, useProfiling: true, profilingOptions: options)`
registers the outermost `ProfilingBehavior`, a TimeProvider, options and a default
singleton `IProfilingContext`. `useAuthorization` independently enables the request-attribute
behavior. With all options enabled the order is:

```text
Profiling -> ExceptionToResult -> Authorization -> Validation -> optional Transaction -> Handler
```

The default is off. `ProfilingOptions` controls the slow-operation threshold.
`ICorrelatedRequest`, correlation-ID sanitization, `OperationProfile`, stage measurements
and outcomes are shared contracts. `ProfilingContext` keeps profiles isolated between
asynchronous flows and restores nested scopes. A service may supply its own context
implementation before registration; Matching uses its profiler through this interface.

Events 1301 (`OperationProfiled`) and 1302 (`SlowOperation`) record total duration,
outcome, sanitized correlation ID, stage descriptions, error codes and exception type
names. The behavior does not persist to a database or log request payloads/model output.
Service-specific stage names, AI adapters, timeout policy and log presentation stay in
the service. See [Matching](../../Services/Matching/README.md#profiling-and-debugging)
and the [shared-library guide](../../../docs/BUILDING_BLOCKS.md).

## Authorization

`[Authorize]` marks a MediatR request as requiring an authenticated `ICurrentUser`.
`[Authorize("claimType", "claimValue")]` additionally requires one exact claim. The
`AuthorizationBehavior` returns 401/403 result errors before validation or handlers run.
Unattributed MediatR requests remain public. HTTP endpoints still need ASP.NET Core
`[Authorize]` or a policy where endpoint admission must be protected.

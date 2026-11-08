# Shared Application building blocks

This library owns reusable MediatR exception, validation, optional transaction and
optional profiling behaviors, plus framework-independent application contracts.
It depends on Core, MediatR, FluentValidation and DI/logging abstractions; it never
references a service, EF Core or ASP.NET Core.

## Profiling opt-in

`AddBuildingBlocksApplication(assembly, useProfiling: true, profilingOptions: options)`
registers the outermost `ProfilingBehavior`, a TimeProvider, options and a default
singleton `IProfilingContext`. With profiling enabled the order is:

```text
Profiling -> ExceptionToResult -> Validation -> optional Transaction -> Handler
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

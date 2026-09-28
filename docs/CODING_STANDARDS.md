# C# standards

The enforceable baseline lives in `.editorconfig`, `Directory.Build.props` and
`Directory.Build.targets`. IDE preferences must not override the repository rules.
CI runs `dotnet format --verify-no-changes` and treats compiler/analyzer warnings as errors.

| Symbol | Convention | Example |
| --- | --- | --- |
| Classes, records, structs, enums | PascalCase | `OrderService`, `OrderStatus` |
| Interfaces | `I` + PascalCase | `IOrderRepository` |
| Methods, properties, events | PascalCase | `CreateOrder`, `OrderId` |
| Parameters and locals | camelCase | `orderId`, `cancellationToken` |
| Private fields | `_` + camelCase | `_orderRepository` |
| Constants | PascalCase | `MaximumRetries` |
| Type parameters | `T` + descriptive PascalCase | `TResponse` |

Use four spaces, LF line endings, a final newline, and no trailing whitespace in code.
Place opening/closing braces on their own lines (Allman style), including control flow.
Always use braces for `if`, `else`, loops and similar blocks. Use `if (condition)` and
`Method(argument)`; do not put spaces inside parentheses. Parenthesize mixed expressions
when needed to make precedence clear. Use file-scoped namespaces, explicit access modifiers,
and predefined type keywords (`int`, `string`). Use `var` only when the type is apparent.

```csharp
namespace Aset.Orders;

public sealed class OrderService
{
    private readonly IOrderRepository _orderRepository;

    public OrderService(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Order> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("An order ID is required.", nameof(orderId));
        }

        return await _orderRepository.GetAsync(orderId, cancellationToken);
    }
}
```

Review conventions beyond the built-in analyzers: name asynchronous methods with `Async`;
pass cancellation tokens through I/O operations; avoid `async void` except event handlers;
prefer dependency injection and small cohesive classes; keep one main type per matching file;
use descriptive names, guard clauses and structured logging. Do not log credentials or
sensitive payloads. Do not use `.Result`/`.Wait()` to block asynchronous request handling.
These architectural/semantic conventions need code review; the formatter cannot prove them.

Use nullable annotations instead of blanket null-forgiving operators. Handle failures at
meaningful boundaries and preserve exception stack traces. Review suppressions individually;
do not disable warnings, analyzers, auditing or coverage across a project to get a green build.

Unit tests should be deterministic, fast and independent of network services. Integration
tests verify service dependencies and contracts using isolated containers where appropriate.
Use descriptive PascalCase test methods such as `CreateOrderRejectsMissingCustomer`.
Keep business logic out of controllers/endpoints so it is straightforward to unit test.

Services own their data and expose explicit versioned contracts. Keep cross-service calls
bounded by timeouts, use health/readiness endpoints, and design migrations to remain compatible
with the previous service version so a container rollback remains possible.

# Design patterns used in SmartLost

This inventory identifies patterns supported by the existing application code,
shared libraries, generated NMF model code and explicit framework usage. Each entry
names the participants and links to their implementation. The presentation allocation
assigns two existing patterns to each of six speakers without introducing new classes
or features. Presentation responsibility does not establish implementation authorship.

## GoF patterns evidenced in the code

| Pattern | Where it appears | Why it fits |
| --- | --- | --- |
| Command | [AuthController](../src/Services/Auth/SmartLost.AuthService.Api/Controllers/AuthController.cs), [RegisterUserCommand](../src/Services/Auth/SmartLost.AuthService.Application/Commands/Register/RegisterUserCommand.cs), [RegisterUserCommandHandler](../src/Services/Auth/SmartLost.AuthService.Application/Commands/Register/RegisterUserCommandHandler.cs) | A request represents a use-case action, while a separate handler executes it. The controller sends it through MediatR. This is the command/handler variant; undo is not implemented. Login and refresh follow the same structure. |
| Mediator | [Controller's ISender](../src/Services/Auth/SmartLost.AuthService.Api/Controllers/AuthController.cs), [MediatR registration](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/DependencyInjection.cs) | Framework dispatch connects requests to registered handlers without the controller depending directly on handler implementations. This is in-process mediation, not a service message bus. |
| Decorator | [ValidationBehavior](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/ValidationBehavior.cs), [ExceptionToResultBehavior](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/ExceptionToResultBehavior.cs) | Framework-composed pipeline wrappers add behavior around the next request delegate. They share the pipeline behavior contract. |
| Chain of Responsibility | [Pipeline registration and ordering](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/DependencyInjection.cs), [validation forwarding](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/ValidationBehavior.cs) | A stage either short-circuits with a result or forwards execution to `next`. This is another view of the same pipeline used for Decorator, not an independent implementation. |
| Adapter | [PasswordService](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Authentication/PasswordService.cs), [IPasswordService](../src/Services/Auth/SmartLost.AuthService.Application/Interfaces/IPasswordService.cs); also [HttpCurrentUser](../src/BuildingBlocks/SmartLost.BuildingBlocks.AspNetCore/Authentication/HttpCurrentUser.cs) | PasswordService translates the application's Hash/Verify contract into ASP.NET Identity hasher calls and converts its verification enum to bool. HttpCurrentUser translates HTTP claims into the application's current-user contract. |
| Strategy | [OutboxProcessor](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Outbox/OutboxProcessor.cs), [IOutboxMessagePublisher](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Outbox/IOutboxMessagePublisher.cs), [AuthOutboxHttpPublisher](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Outbox/AuthOutboxHttpPublisher.cs) | Delivery is delegated through an injected publisher contract so the processor's claiming/retry algorithm does not depend on HTTP. Only one production delivery strategy currently exists; [publisher tests](../tests/Auth/SmartLost.AuthService.UnitTests/AuthOutboxTests.cs) exercise the HTTP implementation. This identifies the substitution boundary, not several implemented transports. |
| Observer | Generated [Listing](../src/Services/Listings/SmartLost.ListingsService.Modeling/Generated/Listing.cs), [ListingCategoriesCollection](../src/Services/Listings/SmartLost.ListingsService.Modeling/Generated/ListingCategoriesCollection.cs), [notification tests](../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsModelTests.cs) | Subscribers attach to property/relationship change events and receive notifications. This is generated NMF/framework behavior, not a handwritten notification service. |
| Iterator | Generated [Listing collection implementations](../src/Services/Listings/SmartLost.ListingsService.Modeling/Generated/Listing.cs) | Child/reference collections override `GetEnumerator()` and return composed enumerators, exposing traversal without their storage details. This is generated and language/framework-backed. |
| Builder | [API Program.cs](../src/Services/Auth/SmartLost.AuthService.Api/Program.cs), [worker Program.cs](../src/Services/Auth/SmartLost.AuthService.Worker/Program.cs) | Host builder objects accumulate configuration before `Build()` produces the application/host. The project consumes framework builders; it does not implement its own builder class. |

## Enterprise and architectural patterns

These are real project patterns and idioms, but they should not be relabeled as GoF
patterns to inflate the laboratory count.

| Pattern | Participants and relationship | Evidence |
| --- | --- | --- |
| Repository | Application persistence contract implemented by an EF-backed repository. | [IUserAccountRepository](../src/Services/Auth/SmartLost.AuthService.Application/Interfaces/IUserAccountRepository.cs), [UserAccountRepository](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/UserAccountRepository.cs) |
| Unit of Work | One EF context tracks related changes and saves them at a single commit boundary. | [AuthDbContext](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/AuthDbContext.cs). The separate [IUnitOfWork](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Transactions/IUnitOfWork.cs) / [TransactionBehavior](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/TransactionBehavior.cs) abstraction exists but is not enabled by Auth. |
| Data Mapper | EF configuration maps domain objects to PostgreSQL tables, columns, constraints and relationships. | [UserAccountConfiguration](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs), [AuthDbContext](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/AuthDbContext.cs) |
| Identity Map | EF context tracking maintains entity identity within a context; repository queries use tracked entities. | [AuthDbContext](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/AuthDbContext.cs), [repository queries](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/UserAccountRepository.cs). Framework-backed, not a custom identity cache. |
| Static Factory | Named creation methods construct entities and success/failure results. | [UserAccount.Create](../src/Services/Auth/SmartLost.AuthService.Domain/Entities/UserAccount.cs), [RefreshSession.Create](../src/Services/Auth/SmartLost.AuthService.Domain/Entities/RefreshSession.cs), [Result factories](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/Results/Result.cs). These are not GoF Factory Method. |
| Dependency Injection | Composition roots supply infrastructure implementations to application abstractions. | [Auth registration](../src/Services/Auth/SmartLost.AuthService.Infrastructure/DependencyInjection.cs), [shared registration](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/DependencyInjection.cs) |
| Ports and Adapters | Application ports isolate persistence/security contracts; infrastructure and HTTP adapters implement or invoke them. | [Application interfaces](../src/Services/Auth/SmartLost.AuthService.Application/Interfaces/), [PasswordService](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Authentication/PasswordService.cs), [repository](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/UserAccountRepository.cs) |
| Entity and Aggregate Root | Entities have identity-based equality; aggregate roots collect domain events; domain operations guard valid changes. | [Entity](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/Entities/Entity.cs), [AggregateRoot](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/Entities/AggregateRoot.cs), [UserAccount](../src/Services/Auth/SmartLost.AuthService.Domain/Entities/UserAccount.cs) |
| Domain Event | Domain operations record facts for later mapping/processing. | [UserAccountCreated](../src/Services/Auth/SmartLost.AuthService.Domain/Events/UserAccountCreated.cs), [UserIdentityChanged](../src/Services/Auth/SmartLost.AuthService.Domain/Events/UserIdentityChanged.cs), [event collection](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/Entities/AggregateRoot.cs). Collecting events alone is not Observer dispatch. |
| Transactional Outbox | Entity changes and selected integration messages commit together; a separate worker delivers pending messages with leases/retries. | [AuthDbContext](../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/AuthDbContext.cs), [OutboxProcessor](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Outbox/OutboxProcessor.cs), [worker](../src/Services/Auth/SmartLost.AuthService.Worker/AuthOutboxJob.cs). No receiving service is implemented. |
| Result object | Handlers return explicit success/error values, adapted to HTTP responses. | [Result](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/Results/Result.cs), [Result of T](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/Results/ResultOfT.cs), [HTTP adapter](../src/BuildingBlocks/SmartLost.BuildingBlocks.AspNetCore/Results/ResultActionResultExtensions.cs) |
| Lazy Initialization | Generated metamodel attributes resolve metadata on demand. | `Lazy<ITypedElement>` fields in generated [Listing](../src/Services/Listings/SmartLost.ListingsService.Modeling/Generated/Listing.cs). This does not establish Singleton or Proxy. |

## AOP related behavior currently present

MediatR's registered validation and domain-exception behaviors centralize cross-cutting
concerns around handler execution. This is explicit pipeline interception. The project
has no configured AspectJ, weaving engine, pointcut declarations, or timing/audit
aspects. The optional transaction behavior exists in the shared library but Auth's
registration leaves it disabled. AOP and Decorator can describe related concerns;
they are not interchangeable pattern names.

## What the current code does not establish

- A state enum is not the State pattern. Listings has a generated `ListingState` enum,
  but no polymorphic state handlers governing transitions.
- `UserAccount.Create` is a static factory, not GoF Factory Method or Abstract Factory.
- `AddSingleton` configures dependency-injection lifetime; it is not proof of a GoF Singleton.
- NMF containment does not alone establish Composite's uniform leaf/container operation.
- A gateway/interface does not alone establish Proxy, Bridge or Facade.
- Inheritance alone does not establish Template Method; the fixed base algorithm must exist.
- The command/handler organization does not establish a complete CQRS architecture.
- Empty Listings/Matching runtime scaffolds do not implement the planned patterns in diagrams;
  their separate NMF Modeling libraries provide generated structural behavior only.

## Six member presentation allocation

Each speaker presents two existing patterns, with no repeated pattern names across
the six pairs: **12 assignments consisting of nine GoF categories and three enterprise
or architectural patterns**. Framework/generated usage is identified explicitly.
This meets a two-pattern presentation allocation when enterprise/architectural patterns
are accepted; it does not establish 12 distinct GoF implementations or two patterns
personally implemented by each member. Use actual contribution history for authorship.

| Speaker | Presentation topic | Pattern 1 | Pattern 2 | Classification |
| --- | --- | --- | --- | --- |
| [Member 1](design-patterns/member1/README.md) | Request dispatch | Command | Mediator | Both GoF, using MediatR's command/handler variant |
| [Member 2](design-patterns/member2/README.md) | Request pipeline and cross-cutting concerns | Decorator | Chain of Responsibility | Both GoF interpretations of the same registered pipeline |
| [Member 3](design-patterns/member3/README.md) | Framework integration and application construction | Adapter | Builder | GoF; application adapter plus framework host builder |
| [Member 4](design-patterns/member4/README.md) | Persistence boundary | Repository | Unit of Work | Both enterprise patterns; EF supplies Unit of Work behavior |
| [Member 5](design-patterns/member5/README.md) | Generated Listings model behavior | Observer | Iterator | Both GoF, generated/framework-backed by NMF and .NET |
| [Member 6](design-patterns/member6/README.md) | Reliable profile-event delivery | Strategy | Transactional Outbox | GoF delivery strategy plus architectural reliability pattern |

Member numbers here identify presentation slots, not the authors of the existing
diagram folders. Replace them with the team's names when assigning speakers. A
suggested format is about three minutes per speaker: explain each pattern's purpose,
show the participants in source, and trace one existing call sequence. Use the order
below to move from incoming requests through persistence to asynchronous delivery.

The detailed speaking notes and source links are separated into
[one folder per member](design-patterns/README.md). Keep individual presentation
notes there and use this document as the shared pattern inventory.

### Coordinator presentation checklist

The team chooses one of the six speakers as coordinator; this document does not assign
that role to a specific person. The coordinator should introduce the current application
scope, verify that each member can explain both assigned patterns from code, and close
with the component handoffs and AOP status. Record actual GitHub contributions before
negotiating member points. Presentation allocation alone is not proof that the original
requirement for two implementations per member has been fulfilled.

## Further reading

- [Repository guide](../README.md)
- [Auth service guide](AUTHSERVICE.md)
- [Shared building blocks](BUILDING_BLOCKS.md)
- [Listings modeling guide](../src/Services/Listings/SmartLost.ListingsService.Modeling/README.md)

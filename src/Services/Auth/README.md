# Auth service

The implemented .NET 10 authentication service provides register, login and refresh
endpoints, JWT issuance, persistent refresh sessions and a PostgreSQL-owned outbox.
Its separate finite worker delivers profile integration events over HTTP.

- [Communication diagrams](COMMUNICATION.md): current request flows, refresh/replay
  handling and atomic outbox staging followed by worker delivery.
- [AuthService guide](../../../docs/AUTHSERVICE.md): layers, API contracts, database,
  migrations, configuration and tests.
- [Worker guide](SmartLost.AuthService.Worker/README.md): receiver contract, safe local
  configuration, build/run commands and optional Compose worker.
- [NMF structural model](SmartLost.AuthService.Modeling/README.md): Ecore/NMeta and generated
  structural representations of Auth accounts, sessions and token history. It is separate from
  the runtime domain and does not serialize credentials or tokens.
- [Repository guide](../../../README.md): complete local stack and CI commands.

Keep the communication diagrams current in the same change as service code,
configuration or tests. If interactions do not change, add a specific explanation
under their review notes. CI checks that the communication document changes; human
review verifies that the arrows and implementation status remain accurate.

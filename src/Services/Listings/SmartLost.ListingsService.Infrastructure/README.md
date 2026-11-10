# Listings Infrastructure and migrations

This project implements Listings-owned EF Core persistence: [ListingsDbContext](Persistence/ListingsDbContext.cs),
[seven entity configurations](Persistence/Configurations/), the [design-time factory](Persistence/ListingsDbContextFactory.cs),
[InitialCreate and model snapshot](Persistence/Migrations/) and opt-in DI registration.
EF Core/tooling is pinned to **10.0.12**, Npgsql EF provider to **10.0.3**.
Domain remains independent of EF and NMF. The API now composes Application read queries
with the EF [ListingsReadStore](Persistence/ListingsReadStore.cs).

## Database ownership and mapping

The service owns the `listingsservice` PostgreSQL database. InitialCreate creates
Listings, Photos, Categories, ListingCategories, MatchDecisions, Recoveries and
CredibilityEntries, plus EF migration history. Auth user IDs are logical values: no
Users table or cross-service foreign key is created.

- ListingCategory uses `(ListingId, CategoryId)` as its composite key.
- Photo.ListingId, Recovery.MatchDecisionId and CredibilityEntry.RecoveryId are unique.
- Listing owns Photo/assignments with cascade deletion; shared Categories and historical
  decision/recovery/award relationships use Restrict.
- Listing Revision/ChangeSequence and MatchDecision State are optimistic concurrency tokens.
- OccurredOn maps to PostgreSQL `date`; audit/publication timestamps map to
  `timestamp with time zone` and require UTC values. Traits use PostgreSQL `text[]`.
- Private setters and List backing fields allow materialization and read-only public views.
- Lookup indexes cover author, listing state/type, category name and award beneficiary.
- Aggregate DomainEvents are ignored. SaveChanges currently persists data only; it does
  not dispatch, stage in Outbox or clear domain events.

Global accepted reservations, consumed Found eligibility, automatic invalidation and
transaction-level use-case orchestration remain application work. Database mapping alone
is not the complete recovery protocol. No Outbox table/worker is added here. Public reads use AsNoTracking projections to DTOs;
filtering, sorting, count and pagination run in SQL. The read adapter does not save data
or call Auth/Matching/Blob Storage. See [query contracts](../SmartLost.ListingsService.Application/README.md).

## Local database and configuration

From the repository root, create ignored local files without overwriting existing ones:

```sh
cp -n .env.example .env
cp -n src/Services/Listings/SmartLost.ListingsService.Api/appsettings.example.json src/Services/Listings/SmartLost.ListingsService.Api/appsettings.json
cp -n src/Services/Listings/SmartLost.ListingsService.Api/appsettings.Docker.example.json src/Services/Listings/SmartLost.ListingsService.Api/appsettings.Docker.json
```

Match the root `.env` LISTINGS_POSTGRES_PASSWORD with both JSON connection passwords.
Local API/EF uses `Host=localhost;Port=5433`; the container JSON uses `Host=listings-db;Port=5432`.
EF reads settings/environment, not `.env`. Infrastructure links the local appsettings.json
into its design-time output, excluding examples and Docker settings; nothing is published.

[Root Compose](../../../../docker-compose.yml) owns the three-service development stack.
Listings keeps a separate PostgreSQL database/user, health check and named volume.
Its API exposes port 5081 and reads appsettings.Docker.json from a read-only bind mount.
Start only its database before applying migrations:

```sh
docker compose config --quiet
docker compose up -d --wait listings-db
```

Stop only Listings without deleting data:

```sh
docker compose stop listings-service listings-db
```

`docker compose down` removes the entire shared stack's containers/network while retaining
volumes. `docker compose down --volumes` is destructive for managed volumes. An adopted
external Listings volume is preserved: set LISTINGS_POSTGRES_VOLUME to its existing name
and LISTINGS_POSTGRES_VOLUME_EXTERNAL=true in root `.env` before migrating an old stack.
Fresh setups leave those variables unset. The current local setup adopts listings_listings-db-data.
Changing the password setting does not rotate credentials inside an initialized volume.
See [API container setup](../SmartLost.ListingsService.Api/README.md#docker-api-and-database).

## EF CLI

Run from the repository root. Infrastructure is both the target and startup project;
its design-time factory works without an executable API or a running database for
schema-only commands.

```sh
dotnet tool restore
dotnet restore src/Services/Listings/Listings.slnx --locked-mode
dotnet ef dbcontext info --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext
dotnet ef migrations list --no-connect --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext
dotnet ef migrations has-pending-model-changes --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext
```

Apply the existing migration after starting the database and configuring credentials:

```sh
dotnet ef database update --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext
```

For a later model change, update the manual Domain entities and EF configurations,
then create and review a new migration (replace `DescribeYourChange`):

```sh
dotnet ef migrations add DescribeYourChange --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext --output-dir Persistence/Migrations
```

Format new migration files with `dotnet format Aset.slnx`, build and run the tests below.
The repository analyzers also apply to migrations; use collection expressions for
constant array arguments if CA1861 flags generated code. No migration runs automatically
from DI registration, API startup or Compose startup.

Generate an idempotent PostgreSQL SQL script offline:

```sh
dotnet ef migrations script --idempotent --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext
```

## Configuration precedence and CI readiness

The factory loads its output-directory appsettings.json by default. Pass
`-- --SettingsFile /absolute/path/appsettings.json` to an EF command for an explicit file;
a missing explicitly requested file fails. It also loads appsettings.{environment}.json,
where DOTNET_ENVIRONMENT takes precedence over ASPNETCORE_ENVIRONMENT and the default
is Development. Environment variables override JSON; arguments after `--` override both.

Use **`ConnectionStrings__ListingsDatabase`** for CI/local environment-based credentials.
A future approved migration job can receive it from GitHub Secrets without writing a
settings file or putting credentials in command arguments. No cloud migration/deployment
job is activated in this change. Schema-only operations have a password-free fallback
`Host=localhost;Port=5433;Database=listingsservice;Username=listingsservice`; it supplies
provider metadata and does not provision or connect to a database.

Future application hosts can call `services.AddListingsPersistence(configuration)`;
it requires a nonblank ListingsDatabase connection and registers a scoped context.
The API invokes it when composing the three public read endpoints. It also registers
IListingsReadStore, the opt-in development seeder and shared in-memory IQueryCache; registration itself does not connect
to the database or execute seeding.

After explicitly applying migrations, [seed.sh](../seed.sh) invokes the API's
Development-only `SeedData:Only` mode to run the same transactional seeder and exit
without starting HTTP. It uses the existing local connection configuration and preserves
fixtures on sequential reruns. See [seed commands and data](../SmartLost.ListingsService.Api/README.md#optional-development-fixtures).

## Verification

- [Unit tests](../../../../tests/Listings/SmartLost.ListingsService.UnitTests/README.md):
  real mapping on isolated SQLite, offline Npgsql migration/script checks, snapshot drift,
  configuration precedence and scoped DI without opening a database connection.
- [Integration tests](../../../../tests/Listings/SmartLost.ListingsService.IntegrationTests/README.md):
  checked-in migration apply, domain graph writes, concurrency, restrictive deletion,
  rollback and reapply on disposable PostgreSQL 16 and 17. Docker required.
- [Communication diagram](../COMMUNICATION.md) and [verification report](../../../../docs/LISTINGS_EF_COMPATIBILITY.md).

## Query cache backend

Listings calls AddBuildingBlocksQueryCache for a singleton MemoryQueryCache. Only the
Application category query is annotated; no Redis, new table or schema migration is
required. See [cache policy and limitations](../../../../docs/QUERY_CACHING.md).

# Listings public read API

The executable .NET 10 API exposes three public read endpoints. It composes Application
queries, the existing MediatR exception/validation pipeline and Infrastructure EF reads.
Only GET /api/categories uses server-side query caching (300-second absolute TTL);
listing list/details remain uncached. There are no JWT-protected mutation endpoints. Public listing reads
always filter to Open; callers cannot request Draft/Closed data through these endpoints.

## HTTP contract

| Method | Route | Success | Other outcomes |
| --- | --- | --- | --- |
| GET | `/api/categories` | 200: array of `{id,name}`, sorted by name then ID | 500: unexpected failure |
| GET | `/api/listings` | 200: `{items,totalCount,currentPage,pageSize}` | 400: invalid filters/paging |
| GET | `/api/listings/{id:guid}` | 200: listing details | 400: empty GUID; 404: malformed route GUID, missing, Draft or Closed |

List query parameters: `type=Lost` or `Found`, optional `categoryId=<guid>`, zero-based
`pageIndex=0` and `pageSize=10` (maximum 100). Type/category filters combine with AND.
Results order by PublishedAt descending, then Id for deterministic page boundaries.
An unknown category or a page beyond the end returns an empty items array; totalCount
still describes the filtered Open records. Invalid enum/GUID text is rejected by model
binding; semantic errors go through the FluentValidation pipeline. Shared Result adapters
produce ProblemDetails for 400/404; unexpected errors use the shared generic 500 handler.

Summaries contain ID/type/title/location/occurrence/publication date, categories and
optional photo metadata. Details also include description, colour, traits and content
revision. Photo metadata contains only ID/format/byte size/revision. No private BlobKey,
image URL or file bytes are exposed. Returning metadata does not imply an uploaded image.

## Local setup

The canonical local connection file is now beside this API. Infrastructure links that
same file to its output for EF CLI; both consume `ConnectionStrings:ListingsDatabase`.
The previous generated local Infrastructure settings were moved to this location when
introducing the API. Only sanitized example JSON is versioned; local settings are excluded from API publish. Environment variables
such as `ConnectionStrings__ListingsDatabase` override local settings.

From the repository root, copy without overwriting existing local credentials:

```sh
cp -n src/Services/Listings/SmartLost.ListingsService.Api/appsettings.example.json src/Services/Listings/SmartLost.ListingsService.Api/appsettings.json
```

Set the database password to match the ignored Listings .env, start the
[database-only Compose stack](../SmartLost.ListingsService.Infrastructure/README.md#local-database-and-configuration),
then explicitly apply its checked-in migration:

```sh
dotnet tool restore
dotnet ef database update --project src/Services/Listings/SmartLost.ListingsService.Infrastructure --startup-project src/Services/Listings/SmartLost.ListingsService.Infrastructure --context ListingsDbContext
dotnet run --project src/Services/Listings/SmartLost.ListingsService.Api --launch-profile http
```

The http launch profile uses **http://localhost:5081** in Development. Swagger UI is at
**http://localhost:5081/swagger/index.html** and OpenAPI at `/openapi/v1.json`, both
Development-only. The launch profile requests opening `/swagger` in a browser when the
launching IDE/tool supports it. The UI loads the `SmartLost Listings API` v1 document
and allows trying all three public GET endpoints without authentication.
Outside Development the API enables HTTPS redirection; the hosting environment supplies
HTTPS configuration. The local Docker stack below runs in Development; no cloud deployment exists.
Startup does not create databases or apply migrations.

## Docker API and database

The [multi-stage Dockerfile](Dockerfile) uses a repository-root build context, locked
restore with SDK 10.0.401 and an ASP.NET 10.0.12 runtime. Both base images are pinned by
multi-platform manifest digest. The final image runs as the built-in non-root `app` user;
the root `.dockerignore` and publish settings exclude local credentials/settings.

Configure the ignored Listings `.env`, start the database and apply the existing migration
as described above. Then, from the repository root:

```sh
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml config --quiet
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml up -d --build
```

[compose.api.yml](../compose.api.yml) adds `listings-service` to the existing database
stack. Its container listens on 8080 and publishes **127.0.0.1:5081**, so Swagger remains
at **http://localhost:5081/swagger**. Stop any locally running API using this port before
starting the container. Compose waits for database health before starting the API;
it does not check schema readiness or apply migrations.

The API receives `ConnectionStrings__ListingsDatabase` at runtime, using
`Host=listings-db;Port=5432` and the password from `.env`. No local appsettings file is
mounted or baked into this image. Use a development password that is valid as an unquoted
Npgsql connection-string value (for example a random hexadecimal password). An existing
database volume retains its original password when `.env` changes. Host-side EF/seed tools
continue to use `Host=localhost;Port=5433` from local JSON/environment configuration.

The override sets Development explicitly so Swagger is available. Seed is off by default.
To run the same idempotent seed against the running migrated database using the built
image, without publishing a second HTTP listener:

```sh
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml run --rm --no-deps listings-service --SeedData:Only=true
```

Inspect logs or stop/remove this stack:

```sh
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml logs --tail 100 listings-service
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml stop
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml down
# Destructive: also deletes the Listings database volume/data.
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml down --volumes
```

`stop` preserves containers and data; `down` removes containers/network and preserves
the volume. A database-only workflow can continue using only `compose.database.yml`.
The Auth root Compose stack is independent; no service calls or shared databases are added.

## Optional development fixtures

After applying the migration, run the standalone [seed script](../seed.sh) from the
repository root:

```sh
sh src/Services/Listings/seed.sh
```

It builds/runs the API project in Release with `--environment Development
--SeedData:Only=true`, reuses its normal connection configuration, seeds and exits without
starting an HTTP listener or opening Swagger. It never prints credentials or migrates
the database. A missing database/schema fails with a nonzero exit code. The script resolves
its own directory and also works when called by absolute path from another directory.
On Windows, run it in Git Bash or use the equivalent native command:

```sh
dotnet run --project src/Services/Listings/SmartLost.ListingsService.Api --configuration Release --no-launch-profile -- --environment Development --SeedData:Only=true
```

To seed during Development API startup and keep serving HTTP instead:

```sh
dotnet run --project src/Services/Listings/SmartLost.ListingsService.Api --launch-profile http -- --SeedData:Enabled=true
```

The Development-only seeder creates/reuses backpack, wallet and keys categories and
creates six fictional listings: two Lost/two Found Open, one Draft and one Withdrawn.
One listing has metadata-only photo information; no file is uploaded. Two fixed fictional
Auth author GUIDs are logical references, not real accounts. Repeated sequential seed
runs preserve records matched by seed author/title and do not duplicate fixtures.
Seeding commits its data in a transaction and never migrates the database. The flag is
off by default and ignored outside Development; explicit `SeedData:Only=true` outside
Development is rejected. There is no public seed endpoint. If an API instance is already
running, its categories cache may retain the previous catalog for up to 300 seconds;
restart that instance or wait for expiry after running the separate seed command.

Try the read flows:

```sh
curl http://localhost:5081/api/categories
curl 'http://localhost:5081/api/listings?type=Lost&pageIndex=0&pageSize=2'
```

Use a returned category/listing ID for category-filter or detail requests. Seed execution
is opt-in; the four Open fixtures are visible, while the other two return 404 by ID.
See [Application CQRS](../SmartLost.ListingsService.Application/README.md),
[communication diagrams](../COMMUNICATION.md) and [service overview](../README.md).

[Query cache details and observation commands](../../../../docs/QUERY_CACHING.md) describe
the opt-in pipeline, cache debug logs and per-process/TTL consistency.

## Local verification

On 2026-11-04, Release build with warnings as errors, info-severity format verification,
78 Listings unit tests and three PostgreSQL integration tests passed. Both Swagger UI and
its JavaScript configuration/document were checked over HTTP, including all three GET
routes. The standalone seed script ran twice against the migrated local PostgreSQL 16
container: three categories, six listings and one photo-metadata row remained, with no
INSERTs on the second run and no HTTP listener started by either run. Public reads
returned three categories and four Open listings. No hosted CI/cloud run was performed.

The Docker image and merged Compose configuration were also built/validated locally on
2026-11-04. An image-based seed run preserved existing fixtures and exited without HTTP.
Because the local dotnet API occupied 5081, a temporary container was checked on loopback
5082: Swagger/OpenAPI, categories, filtered Lost listings and details all returned 200;
two category requests produced one SELECT. Its runtime UID was 1654 and no appsettings
JSON existed in `/app`. The temporary container and the unstarted conflicting API
container were removed afterward; the existing database/volume and local API were preserved.
The normal Compose mapping remains 5081. No hosted image security scan was run locally.

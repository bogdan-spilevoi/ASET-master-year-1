# AuthService

A .NET 10 authentication microservice for the ASET solution. It exposes register and login endpoints, issues JWTs, and stores users in a dedicated PostgreSQL database used only by this service.

## Architecture

The service follows a simple API + application service + data access split:

- `Controllers` handle HTTP requests.
- `Services` handle registration, login, password hashing, and JWT creation.
- `Data` owns the EF Core `DbContext` and the auth-only database schema.
- `Models` and `Contracts` define the user and request/response shapes.

The database is not shared with other services.

## Local development and containers

### Docker files

- [`docker-compose.yml`](../../docker-compose.yml) starts the auth API and its PostgreSQL database.
- [`src/AuthService/SmartLost.AuthService.Api/Dockerfile`](SmartLost.AuthService.Api/Dockerfile) builds the API image.
- [`.env.example`](../../.env.example) contains the local environment values to copy into `.env`.

The Dockerfile uses the repository root as its build context so the API's Application,
Domain and Infrastructure references, shared build configuration and package locks are
available during restore. It uses locked restore and starts `SmartLost.AuthService.Api.dll`.

### Start the stack

```sh
Copy-Item .env.example .env
# edit .env if you want custom secrets
docker compose up --build
```

### Stop the stack

```sh
docker compose stop
```

### Remove the stack

```sh
docker compose down
```

### Remove the stack and volumes

```sh
docker compose down -v
```

### Local URLs

- API via Docker: `http://localhost:8080`
- API via `dotnet run`: `http://localhost:5048` or `https://localhost:7085`

## Database

`SmartLost.AuthService.Api` owns the `authservice` PostgreSQL database. The connection string is configured through `ConnectionStrings:AuthDatabase`.

## API surface

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Creates a new user and returns a JWT |
| `POST` | `/api/auth/login` | Authenticates an existing user and returns a JWT |

## Quality and CI

AuthService test factories supply their own in-memory JWT and database configuration;
tests do not depend on local appsettings files.

The service uses the repository-wide .NET 10 SDK, nullable reference types, warnings as errors, and locked NuGet restore. Build and formatting checks run through the repository-level CI configuration.

Coverlet measures executable lines. CI exempts interface-only contracts without executable
code from the changed-file coverage requirement, regardless of their folder or filename.
Interfaces with implementations and files containing concrete types still require coverage.

`SmartLost.AuthService.UnitTests` exercises service behavior and API response paths with an isolated
in-memory database, collecting Coverlet coverage. `SmartLost.AuthService.IntegrationTests` verifies the
register/login flow against a fresh in-memory database. Run both from the repository root after
restore and build:

```sh
dotnet test tests/SmartLost.AuthService.UnitTests/SmartLost.AuthService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.AuthService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet test tests/SmartLost.AuthService.IntegrationTests/SmartLost.AuthService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.AuthService.IntegrationTests
```

## Further reading

- [Root repository README](../../README.md)
- [API request examples](SmartLost.AuthService.Api/SmartLost.AuthService.Api.http)

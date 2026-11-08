# Matching PostgreSQL integration tests

This suite requires Docker. Testcontainers starts a disposable PostgreSQL 16 database
with random loopback ports and test-only credentials, applies the checked-in InitialCreate
migration and removes the database afterward. No application database/settings are used.

The API runs in Development because its current endpoints are development-only. Tests
replace the photo reader with static mock bytes and use the existing mock AI adapter.
No real Listings, AI or external receiver is contacted.

- Two HTTP requests are synchronized after reading the same Pending task. PostgreSQL
  xmin permits one claim; the loser receives HTTP 409 `matching.concurrent_update`.
  Exactly one embedding is committed and the task is Ready after one attempt.
- An active final lease returns 409. Once its clock advances past expiry, the task becomes
  Failed with `matching.attempts_exhausted`, retains its attempt count and stores no vector.
  Repeating the request preserves the terminal state.

From the repository root:

```sh
dotnet restore src/Services/Matching/Matching.slnx --locked-mode
dotnet build src/Services/Matching/Matching.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Matching/SmartLost.MatchingService.IntegrationTests/SmartLost.MatchingService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.MatchingService.IntegrationTests
```

The existing GitHub Actions integration-project discovery runs this suite on Linux.
See [service communication](../../../src/Services/Matching/COMMUNICATION.md) and
[unit regressions](../SmartLost.MatchingService.UnitTests/README.md).

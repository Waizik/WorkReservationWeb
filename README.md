# WorkReservationWeb

Lightweight reservation platform built for Azure serverless hosting.

## Domain overview

The application manages bookable services, the time slots offered for those services, and the reservations customers create against those slots.
Admins maintain the service catalog and review reservations, while customers browse active services, choose an available slot, and submit a booking.

## Current status

Initial implementation bootstrap is complete:

- .NET 10 mono-repo solution scaffolded.
- Blazor WebAssembly frontend project created.
- Azure Functions backend project created.
- Shared contracts, domain entities, and infrastructure service layer created.
- Public and admin API skeleton endpoints implemented.
- In-memory reservation service added to demonstrate capacity-aware booking with ETag-style conflict behavior.
- Blazor customer booking page implemented with service/slot selection and booking submission.
- Blazor admin page implemented with reservation review and service-offer create/edit/deactivate/delete management.
- Cosmos DB persistence implementation added behind the same service interface.
- Functions startup now uses Cosmos when configured and falls back to in-memory storage otherwise.
- Integration test coverage now exercises the in-memory reservation flow across public booking and admin reservation listing endpoints.
- Opt-in integration coverage now validates the Cosmos transactional booking path against a real Cosmos endpoint or emulator.
- Browser end-to-end coverage now exercises the localhost booking flow and admin service-offer management flow with Playwright.

## Solution structure

- src/WorkReservationWeb.slnx
- src/WorkReservationWeb.Web
- src/WorkReservationWeb.Functions
- src/WorkReservationWeb.Shared
- src/WorkReservationWeb.Domain
- src/WorkReservationWeb.Infrastructure
- tests/WorkReservationWeb.Functions.Tests
- tests/WorkReservationWeb.Integration.Tests
- tests/WorkReservationWeb.Browser.Tests

## API routes currently available

Public:

- GET /api/public/services
- GET /api/public/services/{serviceOfferId}/slots
- POST /api/public/reservations

Admin (requires SWA principal header in this skeleton):

- GET /api/management/services
- GET /api/management/reservations
- DELETE /api/management/services/{serviceOfferId}
- POST /api/management/services

Admin endpoints now require an `x-ms-client-principal` header containing a valid Azure Static Web Apps client principal with the `admin` role.

## Booking conflict behavior in current skeleton

The in-memory booking flow enforces:

- required input validation,
- slot ETag comparison,
- capacity checks,
- explicit outcomes: created, validation failed, conflict.

This is a temporary implementation used to shape API contracts before Cosmos DB integration.

The backend now supports two runtime modes:

- Cosmos mode when `CosmosDb:ConnectionString` is configured.
- In-memory fallback when Cosmos is not configured.

In Cosmos mode, slots and reservations are stored in a single container using `/partitionKey`, where slot and reservation documents for one service share the same partition. Reservation creation updates the slot and creates the reservation in one transactional batch.

## Local development

Prerequisites:

- .NET SDK 10
- Azure Functions Core Tools v4 (for local Functions runtime)

Build everything:

```powershell
dotnet restore src/WorkReservationWeb.slnx
dotnet build src/WorkReservationWeb.slnx
```

Run the Blazor WebAssembly app:

```powershell
dotnet run --project src/WorkReservationWeb.Web/WorkReservationWeb.Web.csproj
```

For local development, the standalone Blazor app is configured to call the Functions host at `http://localhost:7287`.
The development web config also includes a local admin principal header so the `/admin` page can call the admin endpoints without Azure Static Web Apps in front of the Functions host.

The home page now calls the public reservation API routes:

- load active services,
- load available slots for selected service,
- submit reservation request and show result/conflict message.

Run Azure Functions locally:

```powershell
dotnet run --project src/WorkReservationWeb.Functions/WorkReservationWeb.Functions.csproj
```

When both projects are running locally, open the web app at `http://localhost:5273` or `https://localhost:7095` and the booking page will call the Functions API on port `7287`.
The admin page is available at `/admin` and uses the development principal header only in local development.
The admin web UI calls the Functions management endpoints under `/api/management/*` because `/admin/*` is reserved by Azure Functions host internals.

Cosmos configuration for Functions local development:

```json
{
  "Values": {
    "CosmosDb:ConnectionString": "<your-cosmos-connection-string>",
    "CosmosDb:DatabaseName": "WorkReservationWeb",
    "CosmosDb:ContainerName": "Reservations"
  }
}
```

If the Cosmos connection string is left empty, the app uses the in-memory implementation and seeded sample data.

Run tests:

```powershell
dotnet test src/WorkReservationWeb.slnx
```

The browser test project starts the local Functions host and the Blazor app automatically, but Playwright Chromium must be installed once before the browser suite or full solution test run:

```powershell
pwsh tests/WorkReservationWeb.Browser.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Run the opt-in Cosmos integration test against an emulator or disposable test environment:

```powershell
$env:WORKRESERVATION_RUN_COSMOS_TESTS = "true"
$env:WORKRESERVATION_COSMOS_TEST_CONNECTION_STRING = "<your-cosmos-connection-string>"
$env:WORKRESERVATION_COSMOS_TEST_DATABASE = "WorkReservationWebIntegrationTests"
dotnet test tests/WorkReservationWeb.Integration.Tests/WorkReservationWeb.Integration.Tests.csproj --filter CosmosReservationPlatformServiceTests
```

The Cosmos test creates a unique database for each run and deletes it during cleanup.

## Next implementation steps

- Add Blob image upload flow and metadata persistence.
- Add Azure Communication Services email confirmation and reminder processing.
- Add logs for important behaviour.
- Add CI/CD workflows for build/test/deploy.

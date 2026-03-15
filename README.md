# WorkReservationWeb

Lightweight reservation platform built for Azure serverless hosting.

## Current status

Initial implementation bootstrap is complete:

- .NET 10 mono-repo solution scaffolded.
- Blazor WebAssembly frontend project created.
- Azure Functions backend project created.
- Shared contracts, domain entities, and infrastructure service layer created.
- Public and admin API skeleton endpoints implemented.
- In-memory reservation service added to demonstrate capacity-aware booking with ETag-style conflict behavior.
- Blazor customer booking page implemented with service/slot selection and booking submission.
- Cosmos DB persistence implementation added behind the same service interface.
- Functions startup now uses Cosmos when configured and falls back to in-memory storage otherwise.
- Integration test coverage now exercises the in-memory reservation flow across public booking and admin reservation listing endpoints.

## Solution structure

- src/WorkReservationWeb.slnx
- src/WorkReservationWeb.Web
- src/WorkReservationWeb.Functions
- src/WorkReservationWeb.Shared
- src/WorkReservationWeb.Domain
- src/WorkReservationWeb.Infrastructure
- tests/WorkReservationWeb.Functions.Tests
- tests/WorkReservationWeb.Integration.Tests

## API routes currently available

Public:

- GET /api/public/services
- GET /api/public/services/{serviceOfferId}/slots
- POST /api/public/reservations

Admin (requires SWA principal header in this skeleton):

- GET /api/admin/reservations
- POST /api/admin/services

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

The home page now calls the public reservation API routes:

- load active services,
- load available slots for selected service,
- submit reservation request and show result/conflict message.

Run Azure Functions locally:

```powershell
dotnet run --project src/WorkReservationWeb.Functions/WorkReservationWeb.Functions.csproj
```

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

## Next implementation steps

- Extend integration coverage to Cosmos emulator or test-environment validation for the transactional booking path.
- Add Blob image upload flow and metadata persistence.
- Add Azure Communication Services email confirmation and reminder processing.
- Implement admin authentication/authorization with Azure Static Web Apps identity and provider configuration.
- Add CI/CD workflows for build/test/deploy.
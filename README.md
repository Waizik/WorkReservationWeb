# WorkReservationWeb

Lightweight reservation platform built for Azure serverless hosting.

## Domain overview

The application manages bookable services, the time slots offered for those services, and the reservations customers create against those slots.
Admins maintain the service catalog and review reservations, while customers browse active services, choose an available slot, and submit a booking.

## Current status

Initial implementation bootstrap is complete:

- Mono-repo solution scaffolded (SDK pinned to .NET 10; backend projects target .NET 9 for Static Web Apps compatibility, see below).
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
- Service-offer images can now be uploaded through the admin UI, stored in Azure Blob Storage when configured or local file storage otherwise, and served through a public asset endpoint.
- Reservation booking now sends confirmation notifications, and due reminder notifications can be processed both from the admin flow and from a scheduled Azure Function with Azure Communication Services or a local file fallback.
- Important backend behaviors now emit structured logs for reservation creation outcomes, reminder processing, service-offer mutations, and admin image uploads.

## Solution structure

- src/WorkReservationWeb.slnx
- src/WorkReservationWeb.Web
- src/WorkReservationWeb.Functions
- src/WorkReservationWeb.Reminders.Functions
- src/WorkReservationWeb.Shared
- src/WorkReservationWeb.Infrastructure
- tests/WorkReservationWeb.Functions.Tests
- tests/WorkReservationWeb.Integration.Tests
- tests/WorkReservationWeb.Browser.Tests
- infra/main.bicep

## Target frameworks

Azure Static Web Apps managed functions currently support at most `dotnet-isolated:9.0`, so the API and everything it references target .NET 9, while the Blazor WebAssembly frontend ships as static files and can stay on .NET 10:

- `WorkReservationWeb.Functions`, `WorkReservationWeb.Reminders.Functions`, `WorkReservationWeb.Infrastructure`, `WorkReservationWeb.Shared`: `net9.0`.
- `WorkReservationWeb.Web` and the test projects: `net10.0`.

The Functions executables set `RollForward=LatestMajor`, so local runs work with only the .NET 10 SDK installed.
The managed API runtime is pinned in `src/WorkReservationWeb.Web/wwwroot/staticwebapp.config.json` via `platform.apiRuntime = dotnet-isolated:9.0`, which must match the Functions project `TargetFramework`. Once Static Web Apps add `dotnet-isolated:10.0`, bump both together.

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
- GET /api/management/services/{serviceOfferId}/schedule
- POST /api/management/schedules

Admin endpoints now require an `x-ms-client-principal` header containing a valid Azure Static Web Apps client principal with the `admin` role.

In production, `staticwebapp.config.json` additionally protects `/admin` and `/api/management/*` at the Static Web Apps edge with `allowedRoles: ["admin"]`, rewrites unknown URLs to `index.html` for Blazor deep links, and redirects unauthenticated requests to the Entra ID login. The `admin` role is a custom SWA role: after deployment, assign it to users through the Static Web App resource in the Azure portal under Role management (invitations).

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

## Spam protection (optional)

The public booking endpoint can be protected by a [Cloudflare Turnstile](https://developers.cloudflare.com/turnstile/) captcha (free). It is disabled until both keys are configured:

1. Create a Turnstile site at dash.cloudflare.com (widget type "Managed") for your Static Web App domain.
2. Put the **site key** (public) into `src/WorkReservationWeb.Web/wwwroot/appsettings.json` → `CaptchaSiteKey` and commit it.
3. Put the **secret key** into the GitHub secret `CAPTCHA_SECRET_KEY`; the CD pipeline writes it to the `Captcha__SecretKey` app setting. Locally it can be set in `local.settings.json` (`Captcha:SecretKey`).

When enabled, the booking form renders the Turnstile widget and `POST /api/public/reservations` rejects requests without a valid `x-captcha-token` header. When the keys are empty (local development, tests), the captcha is skipped entirely.
Configured in https://dash.cloudflare.com/f13e100c64914e6f7e77c5ac694e696d/turnstile/add - used github account.

The booking endpoint is additionally rate limited per client IP (`x-forwarded-for`). The limit comes from the `RateLimit__ReservationsPerHour` app setting; the CD pipeline sets it from the GitHub variable `RATE_LIMIT_RESERVATIONS_PER_HOUR` (default 10 bookings per hour per IP). Requests over the limit get HTTP 429 before any captcha, database, or e-mail work happens. An empty value disables the limiter (local development, tests). Counters are in-memory per Functions instance — they reset on restart and are not shared across scaled-out instances, which is an accepted trade-off for a free, dependency-less setup.

## Availability schedules

Bookable slots are defined by a weekly availability schedule per service offer, managed in the admin UI ("Availability Schedule" section). The admin picks the days of week and a single set of times that applies to every selected day, plus slot duration, capacity, booking window (how many days ahead customers can book), and time zone. The schedule repeats indefinitely; individual dates can be overridden (custom times or closed entirely).

Slots are virtual: they are computed from the schedule on read and only materialize as slot documents when the first reservation is created (in the same transactional batch as the reservation, using the deterministic id `slot_{yyyyMMddHHmm}` in UTC). Changing the schedule never touches existing reservations — slots that no longer match the schedule simply stop being offered. A service offer without a schedule offers no bookable slots.

## Local development

Prerequisites:

- .NET SDK 10
- Azure Functions Core Tools v4 (for local Functions runtime)

The repository pins the .NET SDK in `global.json`; GitHub Actions and local `dotnet` commands use that file as the SDK source of truth.

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

Blob image storage configuration for Functions local development:

```json
{
  "Values": {
    "BlobStorage:ConnectionString": "<your-blob-connection-string>",
    "BlobStorage:ContainerName": "service-offer-images"
  }
}
```

Images are cropped client-side to a 16:9 frame before upload (Cropper.js, vendored under `wwwroot/lib/cropperjs`) and shown on the public booking page's service cards; services without an image use `wwwroot/images/service-placeholder.svg`.

If the Blob connection string is left empty, uploaded images are stored in a local `uploaded-assets` folder under `%TEMP%/WorkReservationWeb` (outside the Functions script root, which the host watches for changes).

Communication Services configuration for Functions local development:

```json
{
  "Values": {
    "CommunicationServices:ConnectionString": "<your-acs-connection-string>",
    "CommunicationServices:SenderAddress": "DoNotReply@<your-domain>.azurecomm.net"
  }
}
```

If Communication Services is not configured, confirmation and reminder messages are written to a local `sent-emails` folder under `%TEMP%/WorkReservationWeb` (outside the Functions script root, which the host watches for changes).

Reminder schedule configuration for Functions local development:

```json
{
  "Values": {
    "ReservationReminderSchedule": "0 0 0 * * *"
  }
}
```

The scheduled reminder Function uses 6-field NCRONTAB syntax including seconds. The default sample runs once per day at midnight. In NCRONTAB terms, `0 0 0 * * *` means second 0, minute 0, hour 0, every day.

This setting is required for startup. The Functions host now validates `ReservationReminderSchedule` at startup and fails fast if the value is missing or invalid.

After deployment, update the Azure Function App application setting `ReservationReminderSchedule` as well. `local.settings.json` is only used for local development and is not deployed to Azure.

Azure application settings cannot contain `:` in their names, so the deployed settings use the double-underscore form (`CosmosDb__ConnectionString`, `BlobStorage__ContainerName`, ...), which .NET configuration maps back to the `:` hierarchy. The `:` form shown above applies only to `local.settings.json`.

The admin UI button remains available as a manual retry or override path even when the scheduled Function is enabled.

Run tests:

```powershell
dotnet test src/WorkReservationWeb.slnx
```

The browser (Playwright) tests are tagged with the `E2E` trait because they launch the real Functions host, which requires Azure Functions Core Tools and a storage emulator. To run only the dependency-free unit and in-process integration tests:

```powershell
dotnet test src/WorkReservationWeb.slnx --filter "Category!=E2E"
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

## CI/CD

A step-by-step deployment guide (in Czech) is available in [docs/nasazeni-do-azure.md](docs/nasazeni-do-azure.md).

GitHub Actions workflows live under `.github/workflows`.

- `ci.yml` runs on pull requests and branch pushes. It restores, builds, installs Playwright Chromium, and runs the full solution test suite in Release configuration.
- `cd.yml` runs on pushes to `main` and can also be started manually with `workflow_dispatch`. It performs the same validation, deploys Azure infrastructure from `infra/main.bicep`, publishes the Blazor WebAssembly app, publishes the HTTP Functions API as the managed Static Web Apps API, and deploys the scheduled reminder Function to a separate Azure Function App.

The production deployment keeps Azure Static Web Apps on the Free SKU. The HTTP API remains the managed Static Web Apps API so browser calls to `/api/...` keep working without a Standard linked backend. The scheduled reminder runs in a separate Function App because managed Static Web Apps APIs only support HTTP-triggered Functions.

Configure GitHub Actions OIDC before enabling deployment:

- Secret `AZURE_CLIENT_ID`: application/client ID of the federated Azure service principal.
- Secret `AZURE_TENANT_ID`: Azure tenant ID.
- Secret `AZURE_SUBSCRIPTION_ID`: Azure subscription ID.
- Variable `AZURE_RESOURCE_GROUP`: resource group name, defaults to `rg-workreservationweb`.
- Variable `AZURE_LOCATION`: Azure region, defaults to `westeurope`.
- Variable `AZURE_ENVIRONMENT_NAME`: short lowercase name used for Azure resource names, defaults to `workreservationweb`.
- Variable `RESERVATION_REMINDER_SCHEDULE`: NCRONTAB reminder schedule, defaults to `0 0 0 * * *`.
- Optional secret `COMMUNICATION_SERVICES_CONNECTION_STRING`: Azure Communication Services connection string.
- Optional variable `COMMUNICATION_SERVICES_SENDER_ADDRESS`: sender address for Azure Communication Services Email.

The workflow reads the Static Web Apps deployment token from Azure after Bicep creates or updates the Static Web Apps resource, so the old `AZURE_STATIC_WEB_APPS_API_TOKEN` secret is not required for this Bicep-based deployment.

## Azure infrastructure

The Bicep template provisions these production resources:

- Azure Static Web Apps with `Free` SKU.
- A standalone reminder Azure Function App on the Azure Functions Consumption plan.
- A Standard LRS storage account for the Functions runtime and service-offer image blobs.
- A private blob container named `service-offer-images` by default.
- A Cosmos DB account with free tier enabled by default, database `WorkReservationWeb`, and container `Reservations` partitioned by `/partitionKey`.
- A shared Application Insights resource for Functions telemetry.

Deploy the infrastructure manually from an authenticated Azure CLI session:

```powershell
az group create --name rg-workreservationweb --location westeurope
az deployment group create `
  --resource-group rg-workreservationweb `
  --template-file infra/main.bicep `
  --parameters environmentName=workreservationweb location=westeurope
```

The template writes the required reminder Function App settings for Cosmos DB, Application Insights, and `ReservationReminderSchedule`. The CD workflow also writes the managed Static Web Apps API settings for Cosmos DB, Blob Storage, Application Insights, and optional Communication Services after infrastructure deployment.

The Cosmos DB free tier can only be enabled on one Cosmos DB account per Azure subscription. If your subscription already has a free-tier Cosmos account, deploy with `enableCosmosFreeTier=false` or use the existing free-tier account instead.

## Azure pricing notes

Short answer: Azure Static Web Apps is in the Free plan, but the scheduled reminder is not part of that Free SWA allowance.

Azure Static Web Apps Free can host this Blazor WebAssembly frontend and the HTTP-triggered managed API. That managed API feature is limited to HTTP triggers, so the daily `TimerTrigger` reminder cannot run inside the Free SWA managed API. To keep SWA Free, this repository deploys only the scheduled reminder to a separate Azure Function App on the Azure Functions Consumption plan.

Cost components for this deployment:

- Static Web Apps Free plan: no SWA Standard plan charge for the frontend and managed HTTP API.
- Reminder Azure Function App Consumption plan: has Azure Functions Consumption included grants, then bills for executions, execution time, and related meters beyond those grants.
- Cosmos DB free tier: the template enables free tier by default and sets database throughput to 400 RU/s. This is intended to fit inside the free-tier allowance when this is the subscription's one free-tier Cosmos account.
- Storage account: Azure Storage does not have a general free SKU for this use case, so the template uses low-cost Standard LRS storage. It still bills for stored data, transactions, and bandwidth.
- Application Insights: billed separately for telemetry ingestion and retention beyond any included allowance. The Functions `host.json` files default logs to `Warning`, enable sampling, cap sampling at 1 telemetry item per second, and disable live metrics filters to keep ingestion low.
- Azure Communication Services Email: billed separately if configured.

Routine validation and success-path logs are emitted at `Debug` or `Information`, so they are not sent to Application Insights with the default `Warning` threshold. Warnings still capture important conditions such as unauthorized admin attempts and notification delivery failures.

To check whether the deployed Static Web App is Free or Standard:

```powershell
az staticwebapp show `
  --resource-group rg-workreservationweb `
  --name <static-web-app-name> `
  --query sku
```

The Bicep template sets the Static Web Apps SKU to `Free`. In the Azure portal, the same value appears on the Static Web App resource under its hosting plan or SKU details. The reminder Function App is a separate Azure resource, so it has its own Consumption-plan billing independent of the Static Web Apps Free SKU.

## TODO
1, Rate limiter per IP (např. 5 rezervací/hod z jedné IP, počítáno v paměti funkce podle x-forwarded-for)	rychlý flood z jednoho zdroje, i ruční	distribuovaný útok z mnoha IP, sdílené IP (NAT) penalizuje nevinné
2, Byznysová pravidla (max. N budoucích rezervací na jeden e-mail, jedna rezervace na slot a e-mail)

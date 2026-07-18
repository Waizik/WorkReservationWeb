# Návod: Nasazení WorkReservationWeb do Azure

Tento návod popisuje kompletní nasazení projektu do Azure pomocí připravené GitHub Actions pipeline (`.github/workflows/cd.yml`). Ruční alternativa je popsána na konci.

## Co se nasazuje (architektura)

| Komponenta | Azure zdroj | Účel |
|---|---|---|
| Frontend (Blazor WASM) | Static Web Apps, SKU **Free** | Statické soubory webu |
| HTTP API (`WorkReservationWeb.Functions`) | Managed API téhož Static Web App | Veřejné i admin endpointy `/api/...` |
| Plánovaný reminder (`WorkReservationWeb.Reminders.Functions`) | Samostatná Function App, **Consumption** plán | Denní odesílání připomínek (timer trigger — ten managed API v SWA Free neumí) |
| Data | Cosmos DB (free tier), databáze `WorkReservationWeb`, kontejner `Reservations` | Sloty a rezervace |
| Obrázky služeb | Storage account Standard LRS, privátní kontejner `service-offer-images` | Obrázky servírované přes `/api/public/assets/{id}` |
| Telemetrie | Application Insights | Logy (práh Warning + sampling kvůli ceně) |
| E-maily (volitelné) | Azure Communication Services Email | Potvrzení a připomínky rezervací |

Vše provisionuje `infra/main.bicep`; app settings pro SWA managed API zapisuje CD workflow, pro reminder Function App přímo Bicep.

## Prerekvizity

- Azure subscription s právy vytvářet zdroje a Entra ID app registration.
- Repozitář na GitHubu.
- Lokálně Azure CLI (`az`) pro jednorázovou přípravu.

## Krok 1 — Repozitář na GitHubu

Pushněte repozitář na GitHub. Workflows v `.github/workflows` (CI i CD) se aktivují automaticky; CD se spouští pushem na `main` nebo ručně.

## Krok 2 — Service principal s OIDC pro GitHub Actions

CD pipeline se do Azure přihlašuje přes OIDC (bez uložených hesel). V přihlášené `az` session:

```powershell
az login
az account set --subscription "<SUBSCRIPTION_ID>"

# Aplikace + service principal
az ad app create --display-name "workreservationweb-deploy"   # poznamenejte si appId
az ad sp create --id <APP_ID>

# Práva na subscription (nebo na předem vytvořenou resource group)
az role assignment create --assignee <APP_ID> --role Contributor --scope /subscriptions/<SUBSCRIPTION_ID>
```

Federovaný credential pro branch `main` (nahraďte `<OWNER>/<REPO>`):

```powershell
az ad app federated-credential create --id <APP_ID> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<OWNER>/<REPO>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Pokud budete CD spouštět i ručně přes `workflow_dispatch` z jiných refs, přidejte odpovídající další federated credential se stejným issuerem a subjectem daného refu.

## Krok 3 — Secrets a variables v GitHubu

Repo → **Settings → Secrets and variables → Actions**:

Secrets (povinné):

| Název | Hodnota |
|---|---|
| `AZURE_CLIENT_ID` | appId z kroku 2 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | ID subscription |

Variables (volitelné, mají výchozí hodnoty):

| Název | Výchozí |
|---|---|
| `AZURE_RESOURCE_GROUP` | `rg-workreservationweb` |
| `AZURE_LOCATION` | `westeurope` |
| `AZURE_ENVIRONMENT_NAME` | `workreservationweb` |
| `RESERVATION_REMINDER_SCHEDULE` | `0 0 0 * * *` (denně o půlnoci, 6polí NCRONTAB vč. sekund) |

Volitelné pro odesílání e-mailů (bez nich se notifikace ukládají do souborů vedle Functions runtime a nic nespadne):

- secret `COMMUNICATION_SERVICES_CONNECTION_STRING`
- variable `COMMUNICATION_SERVICES_SENDER_ADDRESS` (např. `DoNotReply@<domena>.azurecomm.net`)

## Krok 4 — Kontrola Cosmos DB free tier

Šablona zapíná Cosmos DB **free tier** (1000 RU/s + 25 GB zdarma doživotně). Free tier smí mít jen **jeden** Cosmos účet na subscription. Pokud už jiný free-tier účet máte, nasazení selže — v takovém případě změňte v `infra/main.bicep` výchozí hodnotu parametru `enableCosmosFreeTier` na `false` (a počítejte s cenou provisioned throughput, viz sekce Náklady).

Existující free-tier účet najdete takto:

```powershell
az cosmosdb list --query "[?enableFreeTier].{name:name, rg:resourceGroup}" -o table
```

## Krok 5 — Spuštění nasazení

Pushněte na `main`, nebo v GitHubu: **Actions → CD → Run workflow**.

Workflow provede:

1. build + kompletní testy (vč. Playwright),
2. `az deployment group create` nad `infra/main.bicep` (vytvoří/aktualizuje všechny zdroje),
3. zápis app settings managed API na SWA (Cosmos, Blob, App Insights, případně ACS — názvy s `__`, App Service nepovoluje `:`),
4. publish a nasazení reminder Function App,
5. publish a nasazení webu + HTTP API do Static Web Apps (deployment token si workflow čte z Azure samo).

## Krok 6 — Přiřazení role `admin` (nutné)

Stránka `/admin` a endpointy `/api/management/*` jsou chráněné rolí `admin` (na SWA edge přes `staticwebapp.config.json` i v backendu kontrolou `x-ms-client-principal`).

Azure portál → váš **Static Web App → Role management → Invite**:

1. Provider: Microsoft Entra ID,
2. e-mail uživatele, doména aplikace, platnost pozvánky,
3. do pole role napište `admin`,
4. vygenerovaný odkaz pošlete uživateli; ten se přes něj přihlásí.

Bez pozvánky se k administraci nikdo nedostane (Free SKU umožňuje max. 25 pozvaných uživatelů).

## Krok 7 — Ověření

```powershell
az staticwebapp list --resource-group rg-workreservationweb --query "[].defaultHostname" -o tsv
```

- Otevřete `https://<hostname>` — veřejná rezervační stránka musí načíst seznam služeb (v čerstvém nasazení s prázdným Cosmosem je seznam prázdný — služby založíte v `/admin`).
- Otevřete `https://<hostname>/admin` — musí proběhnout přesměrování na Entra ID login a s rolí `admin` se otevře administrace.
- Reminder Function App: v portálu na Function App zkontrolujte funkci `ProcessReservationRemindersOnSchedule` (Monitor / Invocations). Ručně lze remindery spustit tlačítkem v admin UI.

## Krok 8 — Volitelně: Azure Communication Services Email

1. Vytvořte ACS resource + Email Communication Service s doménou (Azure managed doména je nejrychlejší).
2. Connection string uložte jako secret `COMMUNICATION_SERVICES_CONNECTION_STRING`, odesílací adresu jako variable `COMMUNICATION_SERVICES_SENDER_ADDRESS`.
3. Znovu spusťte CD workflow (propíše nastavení do SWA i Function App).

## Ruční nasazení bez GitHub Actions (alternativa)

```powershell
az group create --name rg-workreservationweb --location westeurope
az deployment group create `
  --resource-group rg-workreservationweb `
  --template-file infra/main.bicep `
  --parameters environmentName=workreservationweb location=westeurope

dotnet publish src/WorkReservationWeb.Web/WorkReservationWeb.Web.csproj -c Release -o artifacts/web
dotnet publish src/WorkReservationWeb.Functions/WorkReservationWeb.Functions.csproj -c Release -o artifacts/api
dotnet publish src/WorkReservationWeb.Reminders.Functions/WorkReservationWeb.Reminders.Functions.csproj -c Release -o artifacts/reminders
```

Reminder Function App nasadíte přes `func azure functionapp publish <nazev>` nebo zip deploy; web + API přes [SWA CLI](https://azure.github.io/static-web-apps-cli/) (`swa deploy artifacts/web/wwwroot --api-location artifacts/api --deployment-token <token>`; token: `az staticwebapp secrets list`). App settings SWA je pak nutné nastavit ručně podle kroku v `cd.yml` (sekce „Deploy Azure infrastructure“). GitHub Actions cesta je jednodušší a doporučená.

## Řešení problémů

- **Nasazení Bicepu selže na Cosmos free tier** → viz krok 4.
- **`/admin` vrací 401/403 po přihlášení** → uživatel nemá roli `admin`, viz krok 6.
- **API vrací 500 hned po nasazení** → zkontrolujte app settings SWA (`az staticwebapp appsettings list -n <nazev>`) — musí obsahovat `CosmosDb__ConnectionString` atd. (s dvojitým podtržítkem).
- **Verze runtime API** — `staticwebapp.config.json` má `platform.apiRuntime = dotnet-isolated:9.0` a musí odpovídat `TargetFramework` projektu Functions; SWA zatím .NET 10 nepodporuje.

## Náklady (malý provoz, orientačně)

| Komponenta | Cena při malém loadu |
|---|---|
| Static Web Apps Free (web + managed API) | 0 Kč (100 GB přenosu/měsíc, bez SLA) |
| Cosmos DB s free tier | 0 Kč (400 RU/s a pár GB se vejde do 1000 RU/s + 25 GB zdarma) |
| Cosmos DB **bez** free tier | ~ 550–600 Kč/měs. (400 RU/s provisioned, ~24 USD) |
| Reminder Function App (Consumption) | ~ 0 Kč (30 spuštění/měs., grant je 1M spuštění + 400 000 GB‑s) |
| Storage account (LRS) | jednotky Kč/měs. (transakce Functions runtime + malé objemy blobů) |
| Application Insights | ~ 0 Kč (práh Warning + sampling; ingest se vejde do 5 GB/měs. zdarma) |
| ACS Email (volitelné) | ~ 0,006 Kč/e-mail — při desítkách e-mailů zanedbatelné |

**Celkem s free tier: prakticky 0–50 Kč měsíčně.** Jediná velká položka hrozí u Cosmos DB bez free tieru — pak zvažte serverless režim (viz doporučení v hlavní dokumentaci / od autora nasazení).

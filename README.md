# job-autopilot

A personal tool for a structured job search in Germany: it fetches vacancies, ranks direct employers above
recruitment agencies, drafts tailored cover letters with the Claude API and tracks every application.
Nothing is ever sent without explicit human approval of that exact application.

**Status:** step 02 of the [roadmap](docs/ROADMAP.md) — vacancies are imported from the Bundesagentur für Arbeit
Jobsuche API on demand and shown in the list (step 01: solution, PostgreSQL, `/health`, vacancy API and page, CI).
Agent rules: [AGENTS.md](AGENTS.md).

## Stack

.NET 10 · ASP.NET Core (minimal APIs) · EF Core + PostgreSQL 17 · Nuxt 4 · Vue 3 · TypeScript · SCSS ·
xUnit v3 + FluentAssertions + Testcontainers · Vitest · Docker Compose · GitHub Actions (Azure later)

## Architecture

```
 ┌───────────────────┐   HTTP/JSON   ┌──────────────────────────────────────────────┐
 │ frontend (Nuxt 4) │ ────────────▶ │ JobAutopilot.Api  endpoints, DTOs, DI, config│
 │ localhost:3000    │               │        │                                     │
 └───────────────────┘               │        ▼                                     │
                                     │ JobAutopilot.Infrastructure  EF Core, Npgsql,│
                                     │        │                     external clients│
                                     │        ▼                                     │
                                     │ JobAutopilot.Core  entities, enums, rules    │
                                     │                    (references nothing)      │
                                     └──────────────────────┬───────────────────────┘
                                                            ▼
                                                  PostgreSQL 17 (Docker)
```

Dependencies point inwards: `Api → Infrastructure → Core`.
Endpoints are **minimal APIs grouped per feature** (`Features/Vacancies/VacancyEndpoints.cs`) — less ceremony than
controllers, with typed results that produce an accurate OpenAPI document.
More background: [ADR 0001](docs/decisions/0001-foundation.md) (foundation),
[ADR 0002](docs/decisions/0002-vacancy-import.md) (import), [Jobsuche API notes](docs/arbeitsagentur-api.md).

## Run locally (Windows, PowerShell)

Prerequisites: .NET 10 SDK, Node 24, Docker Desktop.

```powershell
Copy-Item .env.example .env                    # local DB credentials (never committed)
docker compose up -d db                        # PostgreSQL 17 on localhost:5432

dotnet run --project backend/src/JobAutopilot.Api
# → http://localhost:5080/health, http://localhost:5080/openapi/v1.json
# migrations are applied automatically in Development

cd frontend; npm install; npm run dev          # → http://localhost:3000
```

### Import vacancies from the Arbeitsagentur

Runs the searches configured in `Import:Searches` (`appsettings.json`) and stores new postings; running it again
only updates the known ones (nothing you marked as seen is reset):

```powershell
Invoke-RestMethod -Method Post http://localhost:5080/api/imports/arbeitsagentur
# first run:  fetched 3, added 3, updated 0   (real run on 2026-10-16)
# second run: fetched 3, added 0, updated 3
```

One ad-hoc search instead of the configured ones:

```powershell
Invoke-RestMethod -Method Post http://localhost:5080/api/imports/arbeitsagentur -ContentType 'application/json' `
  -Body '{ "keyword": "Softwareentwickler", "location": "Regensburg", "radiusKm": 30, "publishedWithinDays": 14 }'
```

Paging, delay and retries are configured in the `Arbeitsagentur` section; see [the API notes](docs/arbeitsagentur-api.md).

### Create a vacancy by hand

```powershell
Invoke-RestMethod -Method Post http://localhost:5080/api/vacancies -ContentType 'application/json' `
  -Body '{ "title": "Senior .NET Developer", "company": "Beispiel GmbH", "location": "Berlin", "employerType": "Direct" }'
```

If you change the credentials in `.env`, set the matching connection string as a user secret:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=jobautopilot;Username=…;Password=…" `
  --project backend/src/JobAutopilot.Api
```

### API

| Method | Path | |
|---|---|---|
| GET | `/health` | `{ "status": "ok", "checks": { "database": "Healthy" } }`, 503 if the DB is unreachable |
| GET | `/api/vacancies?page=1&pageSize=20` | newest first, `pageSize` ≤ 100 |
| GET | `/api/vacancies/{id}` | one vacancy |
| POST | `/api/vacancies` | create a manual vacancy; validation errors as ProblemDetails |
| POST | `/api/imports/arbeitsagentur` | import: empty body = configured searches, or one `{ keyword, location, radiusKm, publishedWithinDays }`; 502 if the API is unreachable |

### Database migrations

```powershell
dotnet tool restore
dotnet ef migrations add <DescriptiveName> --project backend/src/JobAutopilot.Infrastructure `
  --startup-project backend/src/JobAutopilot.Api --output-dir Persistence/Migrations
```

## Tests

```powershell
dotnet test backend/JobAutopilot.sln           # unit + integration tests
cd frontend; npm test; npm run typecheck
```

Integration tests run the real API in memory (`WebApplicationFactory`) against **real PostgreSQL started by
Testcontainers**, so constraints, enum mapping and migrations are tested exactly as they run in production.
External services are never called from tests: the Arbeitsagentur client is tested with a fake
`HttpMessageHandler` and hand-written fixtures (`tests/JobAutopilot.Tests/Fixtures/arbeitsagentur/`), the endpoint
with a fake `IVacancySource`. Without Docker the integration tests are skipped locally (in CI they fail instead). To use an existing PostgreSQL server instead
of a container — a temporary database is created and dropped on it:

```powershell
$env:JOBAUTOPILOT_TEST_POSTGRES = "Host=localhost;Port=5432;Username=jobautopilot;Password=jobautopilot"
dotnet test backend/JobAutopilot.sln
```

## Repository layout

```
backend/   JobAutopilot.sln, src/{Api,Core,Infrastructure}, tests/JobAutopilot.Tests
frontend/  Nuxt 4 app (app/pages, app/components, app/composables)
docs/      roadmap, architecture decisions, API notes
prompts/   task prompts, one per roadmap step
```

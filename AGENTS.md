# AGENTS.md — job-autopilot

Rules for any coding agent (Claude Code, Codex, Cursor …) working in this repository.
Read this file fully before changing anything. If a rule here conflicts with a prompt, ask before breaking the rule.

## What this project is

A personal tool that helps one developer (the author) run his job search in Germany:

1. **Fetch** vacancies from job sources (first: the Bundesagentur für Arbeit Jobsuche API).
2. **Filter & score** them against his profile (stack, region, employer type — direct employers over recruitment agencies).
3. **Draft** a tailored Anschreiben (and later CV variant) per vacancy with the Claude API.
4. **Review** — The candidate reads, edits and approves every draft in the UI.
5. **Send** the approved application (e-mail first).
6. **Track** status, follow-ups and response statistics.

It is also a portfolio project: the code is shown to employers. Code quality, structure, tests and a clear README matter as much as features.

## Non-negotiable product rules

- **Nothing is ever sent without an explicit human approval** of that exact application in the UI. No "send all", no auto-send on a schedule, no bypass flag. Fetching, scoring and drafting may run automatically; sending may not.
- **No invented facts.** Generated letters may only use facts from the candidate profile (`profile` data) and the vacancy. If a requirement is not covered, the letter says so honestly or leaves it out — never claims experience that does not exist.
- **Personal data stays local/private.** CV data, letters, API keys and e-mail credentials are never committed, logged in full, or sent anywhere except the Claude API call that needs them.
- Recruitment agencies (Personalvermittler, Zeitarbeit) are detected and marked; they are not hidden, but ranked below direct employers.

## Stack

| Layer | Choice |
|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core Web API, C# with nullable enabled |
| Data | EF Core + PostgreSQL (Docker locally); migrations in the Infrastructure project |
| Frontend | Nuxt 4, Vue 3 Composition API, TypeScript (strict), SCSS |
| AI | Anthropic Claude API, called **only from the backend** |
| Tests | xUnit + FluentAssertions (backend), Vitest (frontend) |
| Local infra | Docker Compose (PostgreSQL; later the API itself) |
| CI | GitHub Actions: build + test for backend and frontend |
| Later | Azure (App Service or Container Apps, Azure Database for PostgreSQL, Key Vault) |

Do not add other frameworks, ORMs, UI kits or state libraries without asking.

## Repository layout

```
job-autopilot/
├─ AGENTS.md  CLAUDE.md  README.md
├─ docs/                     roadmap, decisions (ADR), API notes
├─ prompts/                  numbered task prompts for agents (01-…, 02-…)
├─ backend/
│  ├─ JobAutopilot.sln
│  ├─ src/
│  │  ├─ JobAutopilot.Api/             controllers/endpoints, DI wiring, config
│  │  ├─ JobAutopilot.Core/            domain entities, enums, interfaces, business rules — no EF, no HTTP
│  │  └─ JobAutopilot.Infrastructure/  EF Core DbContext, migrations, external clients (Arbeitsagentur, Claude, SMTP)
│  └─ tests/
│     └─ JobAutopilot.Tests/
├─ frontend/                 Nuxt 4 app
└─ docker-compose.yml
```

Dependency direction: `Api → Infrastructure → Core`. **Core references nothing** from the other projects.

## Backend conventions

- One responsibility per class; services behind interfaces defined in Core (`IVacancySource`, `ILetterGenerator`, `IApplicationSender`).
- External HTTP clients via `IHttpClientFactory` / typed clients; never `new HttpClient()`.
- Configuration via `IOptions<T>` classes; secrets via `dotnet user-secrets` locally and environment variables in Docker/Azure. **Never** put a real key in `appsettings*.json`.
- Async all the way, pass `CancellationToken`.
- Return DTOs from the API, never EF entities.
- Errors: ProblemDetails responses; log with structured logging (`ILogger<T>`), no personal data in logs beyond IDs.
- Every external call (Arbeitsagentur, Claude, SMTP) must be mockable; tests never hit the real services.
- EF migrations: create with a descriptive name, never edit an applied migration.

## Frontend conventions

- Nuxt 4 `app/` directory structure, `<script setup lang="ts">`, strict TypeScript, no `any`.
- API base URL from runtime config (`NUXT_PUBLIC_API_BASE`).
- Components small and named by purpose (`VacancyList.vue`, `ApplicationReview.vue`).
- UI language: German labels where they face the job market (Status names like *Gesendet*, *Absage*), English in code.
- Accessible: real buttons/links, labels for inputs, visible focus.

## Domain vocabulary

- **Vacancy** — a job posting from a source (source + external id are unique).
- **EmployerType** — `Direct`, `Agency`, `Temporary`, `Unknown`.
- **Application** — The candidate's application to one vacancy.
- **ApplicationStatus** — `Draft`, `Approved`, `Sent`, `Acknowledged`, `Response`, `Interview`, `TechInterview`, `Offer`, `Rejected`, `NoResponse`, `Withdrawn`
  (UI labels: Entwurf, Freigegeben, Gesendet, Eingangsbestätigung, Rückmeldung, Interview, Tech-Interview, Angebot, Absage, Keine Antwort, Zurückgezogen).
- **Follow-up date** = last action + 7 days while the application is open.

## Working rules for agents

1. Work on the task in the current prompt only. Do not start the next roadmap step on your own.
2. Before coding, state a short plan; after coding, list what changed and how to run/verify it.
3. Keep the build green: `dotnet build`, `dotnet test`, `npm run build` (frontend) must pass before you say you are done.
4. Small, focused commits with clear messages (`feat: …`, `fix: …`, `chore: …`, `docs: …`, `test: …`).
5. If something is unclear or a decision is architectural (new dependency, schema change beyond the task, security), **ask** instead of guessing.
6. Update `README.md` / `docs/` when you change how to run the project or add a decision.
7. Never commit secrets, `.env`, real CVs or generated letters. Check `.gitignore` before committing.
8. Development machine is Windows + PowerShell: give commands that work there.

## Working economically

- **Start every task with [`docs/CODEMAP.md`](docs/CODEMAP.md)** and open only the files it points to. Find symbols
  with grep/glob instead of opening many files. Flows and layers: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
- Don't open generated or vendored files: `node_modules/`, `bin/`, `obj/`, `.nuxt/`, `.output/`, `dist/`, lock
  files, migration `*.Designer.cs` and the model snapshot (unless the task is about migrations), `data/`, fixtures
  larger than needed.
- Tests: while working run only the relevant ones (`dotnet test --filter …`, `npx vitest run <file>` or
  `npx vitest related <file>`). Run the full suites **once** at the end; don't rebuild/test every commit separately.
- Real external calls (Arbeitsagentur, Himalayas, Claude, Ollama) and real imports only when the task is about them
  and the prompt asks for it.
- No screenshots unless the prompt asks. Logs: `docker compose logs --tail 50`, never full dumps.
- Report: max ~40 lines — what changed (bullets), how to verify (commands), decisions to review, CI status. No
  repeated code, no long tables unless asked.
- Keep `docs/CODEMAP.md` and `docs/ARCHITECTURE.md` up to date in the same commit when files, endpoints or config
  keys move or are added.
- If a task turns out clearly bigger than expected, stop after the first part and ask.

## Commands (fill in as the project grows)

```powershell
docker compose up -d                          # everything in Docker (use mode) -> http://localhost:3000
./dev.ps1                                     # dev mode: db in Docker, API + frontend in one window
./scripts/backup-db.ps1                       # dump to ./backups (git-ignored)
Invoke-RestMethod -Method Post http://localhost:5080/api/imports   # start a background import (202 + importId)
dotnet user-secrets set "Claude:ApiKey" "<key>" --project backend/src/JobAutopilot.Api   # Claude key (never in appsettings)
./scripts/draft-top-vacancies.ps1 -Count 3    # documents for the 3 best vacancies without an application (3 Claude calls), console only
./scripts/translate-profile.ps1 [-DryRun]     # draft missing German CV texts (1 Claude call), write profile.json after approval
./scripts/seed-gold-labels.ps1                # gold labels from chat (Passt / Passt nicht), via the API
./scripts/eval-ranking.ps1 [-Model qwen3.5:9b] # ranking evaluation over the labels (step 07c)
./scripts/daily-run.ps1 [-Top 100]            # Tageslauf: import, filters, scoring, local KI, channels, summary (08b)
ollama pull qwen3:8b                          # local model for the requirement extraction (step 07a, optional)
docker compose up -d db                       # start PostgreSQL only
dotnet build backend/JobAutopilot.sln
dotnet test  backend/JobAutopilot.sln
dotnet run --project backend/src/JobAutopilot.Api
cd frontend; npm install; npm run dev
cd frontend; npm test; npm run typecheck; npm run build

# integration tests need Docker (Testcontainers) or an existing server:
$env:JOBAUTOPILOT_TEST_POSTGRES = "Host=localhost;Port=5432;Username=…;Password=…"

# new EF migration
dotnet tool restore
dotnet ef migrations add <DescriptiveName> --project backend/src/JobAutopilot.Infrastructure --startup-project backend/src/JobAutopilot.Api --output-dir Persistence/Migrations
```

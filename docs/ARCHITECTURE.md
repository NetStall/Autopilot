# Architecture

job-autopilot fetches job postings for one developer, scores them against his profile and Suchprofil, and helps
draft, review and track applications. Everything runs locally (Docker or `dev.ps1`); nothing is sent without an
explicit approval in the UI. Where a feature lives in the code: [CODEMAP.md](CODEMAP.md).

## Layers

| Layer | Project | Contains |
|---|---|---|
| Core | `backend/src/JobAutopilot.Core` | entities, enums, interfaces (`IVacancySource`, `IVacancyStore`, `ILetterGenerator`, `IImportLock` …), rules: scoring, fit, detection, Suchprofil, workflow |
| Infrastructure | `backend/src/JobAutopilot.Infrastructure` | EF Core `AppDbContext` + migrations, Arbeitsagentur/Himalayas/Claude clients, PDF renderer, file stores, DI (`DependencyInjection.cs`) |
| Api | `backend/src/JobAutopilot.Api` | minimal-API endpoints per feature (`Features/*`), DTOs, background workers, security middleware, `Program.cs` |
| frontend | `frontend/app` | Nuxt 4 pages, components, composables (API calls), pure `utils/` (tested with Vitest) |

Dependency rule: `Api → Infrastructure → Core`; Core references nothing. The frontend talks only to the API
(`NUXT_PUBLIC_API_BASE`).

## Data flows

**Import → details → scoring/fit → list**
1. `POST /api/imports` (button) or `ImportScheduler` (daily) takes the searches generated from the Suchprofil
   (`SearchProfileImports.Current` → `SearchProfileRules.GenerateSearches`) and queues a job in `ImportJobChannel`.
2. `ImportWorker` takes the import lock and calls `VacancyImportService.ImportAsync` for every `IVacancySource`
   (Arbeitsagentur, Himalayas).
3. Candidates pass `TitleExclusionFilter`, are de-duplicated (`DuplicateKeys`) and stored via `IVacancyStore`.
4. `VacancyDetailsService.FetchAsync` loads descriptions of new/changed vacancies (capped per run).
5. `VacancyEnricher.Apply` sets relevance (`RelevanceScorer`), remote level (`RemoteClassifier`), employer type
   (`EmployerClassifier`), required German (`GermanRequirementDetector`) and rank = relevance − fit penalties
   (`FitAnalyzer` via `VacancyFitService`, with the technology catalog and the reachability rule → `Unreachable`).
6. `GET /api/vacancies` filters/sorts in SQL (`VacancyQueryExtensions`), returns facets; the detail page asks
   `GET /api/vacancies/{id}/fit` for the full „Passt zu mir?" check.

**Suchprofil → generated searches → re-score**
1. `suchprofil.vue` edits; `POST /api/search-profile/preview` shows generated searches and a diff summary.
2. `PUT /api/search-profile` validates (`SearchProfileRules.Validate`), saves a version (`SearchProfileStore`,
   history + rollback) and requests a re-score (`RescoreCoordinator`).
3. `RescoreWorker` (also at start-up when `Relevance:RescoreOnStartup`) runs `VacancyRescoreService.RescoreAllAsync` under the import lock.
4. The next import uses the new searches.

**Applications and tracker import**
1. `POST /api/vacancies/{id}/apply` or `/documents` creates an `Application` (status `Draft`).
2. Documents: `ApplicationDocumentsService` → `ILetterGenerator` (Claude) + `CvBuilder`, checked by `FactGuard`;
   the user edits, previews and approves (`ApplicationWorkflow.Approve`), PDFs via `QuestPdfDocumentRenderer`.
3. Sending is manual; `POST /api/applications/{id}/mark-sent` records it (`ApplicationWorkflow.MarkSent`).
4. Status changes via `POST /api/applications/{id}/status`; follow-up = last action + 7 days.
5. `POST /api/applications/import-tracker` reads the old tracker file (`TrackerImporter`, `ApplicationMatcher`).

## External systems

| System | Client | Notes |
|---|---|---|
| Bundesagentur für Arbeit Jobsuche | `Infrastructure/Arbeitsagentur/ArbeitsagenturClient` | search + details, typed `HttpClient` |
| Himalayas | `Infrastructure/Himalayas/HimalayasClient` | remote jobs, HTML → text (`HtmlText`) |
| Claude API | `Infrastructure/Claude/ClaudeLetterGenerator` | letters only; key via user-secrets/env; prompt `Prompts/letter.v1.md` |
| Ollama | — | planned, not implemented |

All are behind Core interfaces and faked in tests (`tests/.../Fakes`, stub `HttpMessageHandler`s).

## Background jobs and locks

- `ImportWorker` — consumes `ImportJobChannel`, one import at a time; progress in `ImportRun` rows.
- `ImportScheduler` — daily auto-import (`Import:Schedule`, `DailySchedule`, Europe/Berlin).
- `RescoreWorker` — re-scores on request and at start-up.
- `SearchProfileInitializer` — seeds the Suchprofil on first start (`SearchProfileDefaults`, legacy `preferences`).
- `PostgresImportLock` — PostgreSQL advisory lock shared by import and re-score, works across processes.

## Security

`ApiSecurity`: CORS for configured origins only, security headers, rate limit on imports, ProblemDetails errors.
Frontend CSP in `frontend/server/plugins/content-security-policy.ts`. Details: [SECURITY.md](SECURITY.md).

## Decisions (ADR)

- [0001](decisions/0001-foundation.md) — foundation: stack, layers, Docker.
- [0002](decisions/0002-vacancy-import.md) — vacancy import from the Arbeitsagentur.
- [0003](decisions/0003-vacancy-list-ui.md) — vacancy list UI.
- [0004](decisions/0004-title-prefilter.md) — title pre-filter.
- [0005](decisions/0005-single-import-lock.md) — single import lock.
- [0006](decisions/0006-background-import-queue.md) — background import queue.
- [0007](decisions/0007-daily-auto-import.md) — daily auto-import.
- [0008](decisions/0008-second-source-himalayas.md) — second source Himalayas.
- [0009](decisions/0009-search-profile.md) — Suchprofil.
- [0010](decisions/0010-application-documents.md) — application documents.
- [0011](decisions/0011-requirement-importance-and-german.md) — requirement importance, required German.
- [0012](decisions/0012-technology-catalog-and-reachability.md) — technology catalog, profile-gated keywords, reachability.

Schema: [schema.md](schema.md). Roadmap: [ROADMAP.md](ROADMAP.md).

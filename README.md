# job-autopilot

A personal tool for a structured job search in Germany: it fetches vacancies, ranks direct employers above
recruitment agencies, drafts tailored cover letters with the Claude API and tracks every application.
Nothing is ever sent without explicit human approval of that exact application.

## Quick start

Prerequisite for both: [Docker Desktop](https://www.docker.com/products/docker-desktop/) running.

```powershell
docker compose up -d     # "use" mode: db + api + frontend in Docker → http://localhost:3000
./dev.ps1                # "dev" mode: db in Docker, API (dotnet watch) + frontend (nuxt dev) in one window
```

Dev mode additionally needs the .NET 10 SDK and Node 24; Ctrl+C stops API and frontend. Details below.

**Status:** step 04 of the [roadmap](docs/ROADMAP.md) — vacancies from the Bundesagentur für Arbeit (region around
Deggendorf + home-office jobs in all of Germany, with full descriptions) and from the
[Himalayas](https://himalayas.app) remote jobs API, **scored against the profile** (C#/.NET/Vue: relevant; Java,
PHP, SAP, embedded …: not), with a remote level (voll remote / hybrid), detected recruitment agencies and merged
duplicates. Background import with progress and a daily run at 07:00, fast triage (seen / Merken / Ausblenden,
keyboard shortcuts), filters in the URL; one-command start in Docker or dev mode; security review in
[docs/SECURITY.md](docs/SECURITY.md). Earlier steps: foundation (01), import (02), list UI (03).
Agent rules: [AGENTS.md](AGENTS.md).

![Vacancy list with filters, "Neu" markers and employer type badges (demo data)](docs/img/vacancy-list.png)

<sub>Demo data with fictional companies. Dark mode: [screenshot](docs/img/vacancy-list-dark.png).</sub>

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
[ADR 0002](docs/decisions/0002-vacancy-import.md) (import), [ADR 0003](docs/decisions/0003-vacancy-list-ui.md)
(list UI), [ADR 0004](docs/decisions/0004-title-prefilter.md) (title pre-filter),
[Jobsuche API notes](docs/arbeitsagentur-api.md), [ADR 0005](docs/decisions/0005-single-import-lock.md) (one import
at a time), [ADR 0006](docs/decisions/0006-background-import-queue.md) (background import queue),
[ADR 0007](docs/decisions/0007-daily-auto-import.md) (daily import),
[ADR 0008](docs/decisions/0008-second-source-himalayas.md) (second source: Himalayas). Security: [docs/SECURITY.md](docs/SECURITY.md) — threat model, review findings, and the rule that
**authentication comes before anything is reachable from outside localhost**.

## Run locally (Windows, PowerShell)

`.env` holds the local database credentials and the ports (copied from `.env.example` by `dev.ps1`; never
committed). All ports are bound to `127.0.0.1` only — nothing is reachable from the network
([docs/SECURITY.md](docs/SECURITY.md)).

### Use mode — everything in Docker

```powershell
Copy-Item .env.example .env            # once
docker compose up -d                   # builds the images on first start, waits for health checks
# → UI http://localhost:3000 · API http://localhost:5080/health/ready
docker compose logs -f api             # logs
docker compose down                    # stop (data stays in the "pgdata" volume)
docker compose up -d --build           # after pulling new code
```

The API container runs as non-root, applies migrations on start (`Database__MigrateOnStartup=true`) and runs in
`Production` mode. `NUXT_PUBLIC_API_BASE` is read by the frontend **at runtime**; it is the URL the *browser* uses
to call the API (all data is loaded client-side), so in compose it is `http://localhost:${API_PORT}` — the
published port, not the container name. CORS on the API allows exactly `http://localhost:${WEB_PORT}`.

### Dev mode — one window

```powershell
./dev.ps1                              # or: ./dev.ps1 -ApiPort 5081 -WebPort 3001
```

The script checks Docker / .NET / Node / `.env`, starts the `db` container and waits until it is healthy, then runs
the API with `dotnet watch` and the frontend with `nuxt dev` with prefixed output (`[api]`, `[web]`). **Ctrl+C** stops
both process trees; the database keeps running (`docker compose stop db`). It refuses to start if a port is already
in use (e.g. while use mode is running). Works in Windows PowerShell 5.1 and PowerShell 7. If scripts are blocked:
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

Manual alternative: `docker compose up -d db`, `dotnet run --project backend/src/JobAutopilot.Api`,
`cd frontend; npm install; npm run dev`. Migrations are applied on start when `Database:MigrateOnStartup` is
`true` (it is in `appsettings.Development.json`).

### Backup and restore

```powershell
./scripts/backup-db.ps1                                   # → ./backups/jobautopilot-<timestamp>.sql (git-ignored)
./scripts/restore-db.ps1 ./backups/jobautopilot-20261018-213850.sql   # replaces all data, asks first
```

Stop the API before a restore. The restore runs in one transaction — on an error nothing changes.

### Import vacancies

Click **Import starten** in the UI — it runs in the background with a progress bar — or:

```powershell
$run = Invoke-RestMethod -Method Post http://localhost:5080/api/imports        # 202 → { importId }
Invoke-RestMethod "http://localhost:5080/api/imports/$($run.importId)"         # status, current search, counts
Invoke-RestMethod "http://localhost:5080/api/imports?limit=5"                   # recent runs
# real run on 2026-10-19 (20 searches incl. 5 Himalayas): see docs/arbeitsagentur-api.md, "After step 04"
```

Only one import runs at a time (a second start gets 409). In use mode (docker compose) an import also runs **every
day at 07:00 Europe/Berlin**; if the PC was off at 07:00, the missed run starts when the API starts
(`Import:Schedule`, see [ADR 0007](docs/decisions/0007-daily-auto-import.md)). The scheduler only fetches — it
never sends anything.

It runs every search in `Import:Searches` against its source and stores new postings. Known ones are only updated;
nothing you marked is reset. Every vacancy remembers which searches found it (`matchedSearches`). After the searches
the import fetches the **descriptions** of new or changed Arbeitsagentur postings (`Import:Details`: at most 300 per
run, 300 ms apart, resumable — the rest follows next time), then everything is scored. A source that is down only
skips its own searches. Himalayas postings past their expiry date are deleted (its terms require it).

#### Configuring searches

The import searches are **generated from the Suchprofil** (see *Suchprofil* below): every search term × every place
(region) and × remote Germany, for each enabled source. The page Suchprofil lists them read-only. `Import:Searches`
and `Import:ExcludeTitleTerms` in `appsettings.json` are only the **seed** of the first Suchprofil; after that they are
ignored. Their format (also used for one ad-hoc search):

```jsonc
"Import": {
  "Searches": [
    // region: keyword around a place
    { "Name": "region-csharp", "Keyword": "Softwareentwickler C#", "Location": "Deggendorf", "RadiusKm": 70 },
    // remote: no Location = all of Germany, only home-office postings
    { "Name": "remote-dotnet", "Keyword": ".NET", "RemoteOnly": true, "MaxPages": 5 }
  ],
  "ExcludeTitleTerms": [ "Ausbildung", "Praktikum", "Werkstudent", "Duales Studium" ]
}
```

| Field | |
|---|---|
| `Name` | unique label (letters, digits, `-`, `_`); shown in the UI filter „Suche" |
| `Keyword` | search text. Bare `C#` / `.NET` match a lot of unrelated postings in a region (licence class C, "net…"); combine them, e.g. `Softwareentwickler C#`, `C# .NET`, `.NET Entwickler` |
| `Location`, `RadiusKm` | place and radius in km; leave `Location` out for all of Germany (`RadiusKm` then is an error) |
| `PublishedWithinDays` | default 30 |
| `Source` | `Arbeitsagentur` (default) or `Himalayas` ([ADR 0008](docs/decisions/0008-second-source-himalayas.md); no `Location`, always remote jobs open to Germany) |
| `RemoteOnly` | only home-office postings, see `Arbeitsagentur:RemoteHomeOfficeFilter` below |
| `MaxPages` | per-search page limit (default `Arbeitsagentur:MaxPages` = 10 × 100 results) |

`ExcludeTitleTerms` is a coarse pre-filter: titles containing one of the terms as a whole word or phrase
(case-insensitive) are not stored and are counted as „ausgeschlossen" (`Ausbildung` excludes „Ausbildung zum
Fachinformatiker", not „Ausbildungsleiter"). Invalid search configuration stops the API at start-up with a clear
message.

`Arbeitsagentur:RemoteHomeOfficeFilter` is the home-office filter of `RemoteOnly` searches: `prozentual_0;nv_true`
(default) = any home office incl. "nach Vereinbarung" — the same as the web app's „Homeoffice" filter;
`prozentual_100` = only postings that state 100 % (about 50× fewer). How remote a vacancy really is
(voll remote / hybrid) is decided per vacancy afterwards, see *Relevance and remote level* below. Details and
measured numbers: [docs/arbeitsagentur-api.md](docs/arbeitsagentur-api.md).

One ad-hoc search instead of the configured ones (same fields, camelCase; `name` defaults to `ad-hoc`):

```powershell
Invoke-RestMethod -Method Post http://localhost:5080/api/imports -ContentType 'application/json' `
  -Body '{ "keyword": "Vue", "remoteOnly": true }'
```

### Profil

Everything the tool says about you comes from **one file**: `data/profile.json` (git-ignored — never commit it).
Start from the example with invented data:

```powershell
New-Item -ItemType Directory -Force data | Out-Null
Copy-Item docs/profile.example.json data/profile.json   # then edit it
```

| Part | |
|---|---|
| `personal` | name, email, phone, city, website/github/linkedin (http/https only) |
| `summary` | short self-descriptions by focus, e.g. `fullstack`, `backend`, `ai` |
| `experience[]` | company, title, `from`/`to` (`YYYY-MM`, `to` empty = today), bullets with `tags` |
| `skills[]` | name, `aliases` (spellings in job ads), `level`: `Production` (at work) · `Project` (own projects) · `Learning` · `None` (a gap), optional `years`, `group` |
| `knownGaps[]` | missing skill + an honest phrasing in `de` **and** `en` |
| `projects[]`, `education[]`, `languages[]` (`A1`…`C2`, `Native`), `certificates[]` | |
| `preferences` | **deprecated** — moved to the Suchprofil. Read once to seed it; afterwards ignored (the API logs a hint while they are still in the file) |

The page **Profil** shows the loaded profile and every validation problem (unknown level, `from` after `to`, missing
gap phrasing …). The file is re-read automatically when it changes; a missing or broken file does not stop the API —
the page says what is wrong. In Docker, `./data` is mounted read-only into the API container. The profile's skills
also count as (small) positive keywords in the relevance score.

### Suchprofil

*What I am looking for* — in the database, edited on the page **Suchprofil** (`profile.json` stays *who I am*).
Decision and migration: [ADR 0009](docs/decisions/0009-search-profile.md).

| Section | |
|---|---|
| Suche | search terms (each „Region + Remote", „Nur Region" or „Nur Remote"), places with radius, „Remote deutschlandweit", further places that count as my region, published within X days, sources Arbeitsagentur / Himalayas |
| Remote und Arbeitgeber | Remote-Präferenz *egal* / *mindestens Hybrid* / *nur voll remote*; Personalvermittlung and Zeitarbeit: *zulassen* / *niedriger einordnen* / *ausblenden* |
| Stack | Must-have, Nice-to-have, Ausschluss — keywords with weight, other spellings, group, „nur im Titel", case-sensitive, core stack; Titel-Ausschlüsse (skipped on import) |
| Bewertung | default Mindestbewertung and N of „Nur Top N" for the list; fit penalties (0–50) |
| Rahmen | salary target, earliest start (orientation only, not scored yet) |

While editing, a debounced preview shows „Mit diesen Einstellungen: 142 passende Stellen (davon 31 voll remote)"
without saving. **Speichern** validates (German messages), stores a new version (409 if it was saved elsewhere in the
meantime), re-scores everything in the background and offers **Jetzt importieren** — new terms and places only find
vacancies on the next import. Leaving the page with unsaved changes asks first. **Verlauf** keeps the last 50
versions with a change summary; *Wiederherstellen* saves an old one as a new version. *Als JSON exportieren* /
*JSON importieren* move it between machines (an imported file is checked first and loaded as unsaved changes).

```powershell
Invoke-RestMethod http://localhost:5080/api/search-profile | Select-Object version, @{ n = 'searches'; e = { $_.searches.Count } }
Invoke-WebRequest http://localhost:5080/api/search-profile/export -OutFile "$env:TEMP\suchprofil.json"   # private: places, salary — do not commit
```

### Relevance, remote level and agencies

Every vacancy gets a **score 0–100**, a level (**Passt** ≥ 55, **Vielleicht** ≥ 30, **Passt nicht**) and the reasons
(„Warum?" on the card, full list on the detail page), e.g. `C# im Titel ✓ (+50)`, `Java als Hauptsprache im Titel ✗
(-60)`, `Java neben C#/.NET im Titel – abgeschwächt (-15)`. The list shows *Passt* + *Vielleicht* by default, best
score first; at the same score direct employers come before unknown ones, agencies and temporary work.

The **keyword lists** come from the Suchprofil (Must-have / Nice-to-have / Ausschluss, plus the profile's skills as
small bonuses). The **mechanics** stay in `appsettings.json`:

| Section | What |
|---|---|
| `Relevance` | `Positive` / `Negative` keyword rules — only the seed of the Suchprofil's Stack lists; `GroupCaps` (related skills share a maximum, e.g. `dotnet: 50` — „.NET" in the title plus C# plus ASP.NET does not stack without limit), `BaseScore` (10), `TitleFactor` (title counts ×2), `SoftenedFactor`, thresholds `RelevantFrom` / `MaybeFrom` |
| `Remote` | phrases for **voll remote** („100 % remote", „deutschlandweit remote" …), **vor Ort** („kein Homeoffice") and **hybrid** („mobiles Arbeiten", „{n} Tage Homeoffice") |
| `EmployerDetection` | known agencies (FERCHAU, Hays, Brunel …), phrases for agencies („im Auftrag unseres Kunden") and temporary work („Arbeitnehmerüberlassung"), phrases of direct employers („keine Anfragen von Personaldienstleistern") |

Terms match whole words only: `Java` ≠ „JavaScript", `.NET` ≠ „Netzwerk", `C#` ≠ „C++"; spaces and hyphens are
flexible („Full Stack" = „Full-Stack" = „Fullstack"). Saving the Suchprofil re-scores automatically; after changing
`appsettings.json`, click **Neu bewerten** (or `POST /api/vacancies/rescore`); the API also re-scores everything when
it starts. The remote level is recomputed
each time: an explicit phrase decides („anteilig Homeoffice" → hybrid, even if the Arbeitsagentur field says 100 %);
without a phrase the source's data count (`homeofficeprozent` 100 → voll remote, Himalayas → voll remote); being
found by a remote search keeps a vacancy at least hybrid. The employer type comes from the
Arbeitsagentur flags (Arbeitnehmerüberlassung / private Arbeitsvermittlung), then the known agencies, then phrases.

**Passt zu mir?** On the detail page every vacancy is checked against the profile (rules only, no AI): which
requirements I cover (✓ profile level `Production`), partly (~ `Project`/`Learning`) or not (✗, with the honest
phrasing from `knownGaps`). A requirement only in a sentence with „von Vorteil"/„nice to have", or offered as an
alternative to a skill I cover („Angular oder Vue.js"), is optional. Warnings: German level above mine („sehr gute
Deutschkenntnisse" = C1), more years than mine („mehrjährig", „5 Jahre", „Senior"), not fully remote outside the
Suchprofil's places, an employer type set to „niedriger einordnen", a missed Remote-Präferenz. The **default order**
is the *rank* = relevance minus these penalties (Suchprofil „Fit-Abzüge", seeded from `Fit:Penalties`: German 8 per
level, experience 8, required gap 6 each up to 18, region 15, employer type 10, remote wish 10).

**Score in the UI.** Every card and the detail page show the rank as a pill („87"), coloured high / medium / low by
`Ui:ScoreHigh` (70) and `Ui:ScoreMedium` (45) — never by colour alone: the number is always visible, the screen reader
label is „Bewertung 87 von 100" and the tooltip / second line „Relevanz 94 · Abzüge −8". In the list:
**Mindestbewertung** (slider + number, steps of 5, URL `minScore=60`; default from the Suchprofil) and **Nur Top N**
(URL `top=20`, „Top 20 von 258"). The filters Quelle, Relevanz, Arbeitgeber and Arbeitsort show how many vacancies
each option would give under the other active filters. Common requirements that are not in the profile (Angular,
React, Kubernetes, Führerschein …) are listed in `Fit:CommonRequirements`.

The same job from several sources (or posted twice) is shown once — the first one imported — with „auch bei …"
links (key: company + title without „(m/w/d)" and legal forms).

### Triage in the list

| Key | Action |
|---|---|
| `j` / `k` | next / previous vacancy |
| `o` | open the original posting (marks it as seen) |
| `s` | seen ↔ new |
| `m` | Merken (star) |
| `x` | Ausblenden (not interested; hidden unless „Ausgeblendete anzeigen") |
| `?` | help |

Shortcuts are off while an input has focus. Changes appear immediately and are rolled back with a message if the
API refuses. Keywords from `Ui:HighlightKeywords` are highlighted in titles (visual cue only). The last used filters
are remembered per browser.

### Bewerbungen

The page **Bewerbungen** lists every application with its status (*Gesendet*, *Eingangsbestätigung*, *Interview*,
*Absage* …), channel, date and follow-up date (last action + 7 days while *Gesendet* / *Eingangsbestätigung* /
*Rückmeldung*); rows with a due follow-up are highlighted. Filters (status, channel, „Nur Follow-up fällig") live in
the URL; „Details" opens the history, notes and a status change (each change is stored with date and note and
moves the follow-up date). On a vacancy, **Als beworben markieren** records an application pre-filled from it. The
list shows **Beworben** on every vacancy you applied to — also on the same job from another source or a repost with a
similar title at the same company — so you do not apply twice. The tool only *records* applications; it never sends
anything (AGENTS.md).

Import your existing tracker (personal data — `data/` is git-ignored):

```powershell
New-Item -ItemType Directory -Force data/import | Out-Null
# put the export there as data/import/bewerbungen-export.json, then click "Tracker importieren" or:
Invoke-RestMethod -Method Post http://localhost:5080/api/applications/import-tracker
# → { total, imported, updated, unchanged, linkedToVacancy, warnings }
```

The export is `{ "applications": [ { "id", "datum", "firma", "position", "ort", "kanal", "typ", "status", "gehalt",
"kontakt", "kontaktEmail", "link", "letzteAktion", "naechsterSchritt", "notizen", "unterlagen", "sprache" } ] }` with
German status labels. Re-running is safe: rows are matched by `id` and updated, never duplicated; a status you changed
in the tool after the export was made is kept. Unknown status or channel values, or an hourly wage in `gehalt`, end up
in the notes and in the report's `warnings`. Imported applications are linked to stored vacancies by company + title.

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
| GET | `/health/live` | process is up: `{ "status": "ok", "checks": {} }` |
| GET | `/health/ready` | ready to serve: `{ "status": "ok", "checks": { "database": "Healthy" } }`, 503 if the DB is unreachable |
| GET | `/api/vacancies` | paged `{ items, page, pageSize, totalCount, totalMatching, top, facets: { source, remoteLevel, employerType, relevanceLevel } }`, filters below; each facet counts under all *other* filters |
| GET | `/api/vacancies/{id}` | one vacancy, 404 if unknown |
| GET | `/api/vacancies/{id}/fit` | „Passt zu mir?": `{ profileStatus, hasDescription, requirements: [{ name, coverage: Covered\|Partial\|Gap, optional, note }], warnings: [{ text, penalty }], requiredGerman, requiredYears, penalty }` |
| GET | `/api/applications?status=&channel=&followUpDue=true` | applications, newest first, with `statusLabel`, `followUpAt`, `isFollowUpDue` |
| GET | `/api/applications/{id}` | one application with its status history (`events`) |
| POST | `/api/applications` | record an application by hand → 201 (status *Gesendet*) |
| PATCH | `/api/applications/{id}` | edit fields (not the status) |
| DELETE | `/api/applications/{id}` | delete → 204 |
| POST | `/api/applications/{id}/status` | `{ status, note?, at? }` → history event, last action = now |
| POST | `/api/vacancies/{id}/apply` | „Als beworben markieren" → 201, or 200 with the existing application |
| POST | `/api/applications/import-tracker` | import `data/import/bewerbungen-export.json` (idempotent) → report; 400 if the file is missing/invalid |
| GET | `/api/profile` | `{ status: Loaded\|Missing\|Invalid, fileName, errors, warnings, experienceYears, profile }` |
| PATCH | `/api/vacancies/{id}` | `{ "isSeen": bool, "interest": "None" \| "Starred" \| "Dismissed" }` (either or both) → 204; nothing else is patchable |
| POST | `/api/vacancies/seen` | `{ "ids": [...] }` (1–100) → 204, marks them as seen |
| POST | `/api/vacancies` | create a manual vacancy |
| POST | `/api/vacancies/rescore` | re-score all vacancies in the background → 202 with the status |
| GET | `/api/vacancies/rescore` | `{ state: Idle\|Queued\|Running, lastStartedAt, lastFinishedAt, lastScored, lastError }` |
| POST | `/api/imports` | queue an import: empty body = configured searches, or one search `{ name, keyword, location, radiusKm, publishedWithinDays, remoteOnly, maxPages }` → **202** `{ importId }`; 409 while another import is queued/running, 429 after 6/min |
| GET | `/api/imports/{id}` | `{ status: Queued\|Running\|Succeeded\|Failed\|Cancelled, searchesDone, searchesTotal, currentSearch, fetched, added, updated, unchanged, excluded, error, searches: [...] }` |
| GET | `/api/imports?limit=10` | recent runs, newest first (limit 1–50) |
| GET | `/api/imports/searches` | the configured searches (for the UI filter) |
| GET | `/api/settings/ui` | `{ highlightKeywords, scoreHigh, scoreMedium }` from `Ui` |
| GET | `/api/search-profile` | `{ version, updatedAt, profile, searches }` (searches = generated import searches) |
| PUT | `/api/search-profile` | `{ version, profile }` → `{ searchProfile, rescore }`; 400 ValidationProblem (`errors.profile`), 409 if `version` is not current |
| POST | `/api/search-profile/preview` | a draft profile → `{ errors, matching, fullRemote, currentMatching, scored, searches }`; saves nothing |
| GET | `/api/search-profile/history` | `[{ version, savedAt, summary }]`, newest first (last 50) |
| POST | `/api/search-profile/rollback` | `{ version, expectedVersion }` → saves that version again as a new one; 404 / 409 |
| GET | `/api/search-profile/export` | the document as `suchprofil-v{N}.json` |

Query parameters of `GET /api/vacancies`:

| Parameter | |
|---|---|
| `q` | text in title or company (case-insensitive, matched literally) |
| `location` | text in the location |
| `employerType` | `Direct`, `Agency`, `Temporary`, `Unknown` — repeatable |
| `seen` | `true` / `false`; omitted = all |
| `source` | `Arbeitsagentur`, `Himalayas`, `Manual` — repeatable, matches any |
| `remote` | `FullRemote`, `Hybrid`, `OnSite`, `Unknown` — repeatable |
| `relevance` | `Relevant`, `Maybe`, `Irrelevant` — repeatable; not yet scored counts as `Maybe` |
| `search` | name of a configured search — repeatable, matches any |
| `interest` | `None`, `Starred`, `Dismissed` — repeatable; without it, dismissed vacancies are hidden |
| `includeDismissed` | `true` = show dismissed vacancies too |
| `fetchedSince` | ISO timestamp — only vacancies imported at/after it („Neu seit letztem Import") |
| `minScore` | 0–100: rank (relevance minus fit penalties; relevance if not checked yet) at least this; unscored vacancies are left out when > 0 |
| `top` | 1–200: only the best N by rank after all other filters; `totalCount` = min(N, `totalMatching`) |
| `sort` | `relevance` (default: score, then direct employers first, then newest), `published` or `fetched` |
| `page`, `pageSize` | default 1 / 20, `pageSize` ≤ 100 |

Invalid values return `400` ProblemDetails naming the parameter.

### Database migrations

What the columns mean (e.g. `RemoteHint` vs `RemoteLevel`, `ExpiresAt`): [docs/schema.md](docs/schema.md).

```powershell
dotnet tool restore
dotnet ef migrations add <DescriptiveName> --project backend/src/JobAutopilot.Infrastructure `
  --startup-project backend/src/JobAutopilot.Api --output-dir Persistence/Migrations
```

## Tests

```powershell
dotnet test backend/JobAutopilot.sln           # unit + integration tests
cd frontend; npm test; npm run typecheck      # Vitest: URL filters, dates, URL safety, list states
```

Integration tests run the real API in memory (`WebApplicationFactory`) against **real PostgreSQL started by
Testcontainers**, so constraints, enum mapping and migrations are tested exactly as they run in production.
External services are never called from tests: the Arbeitsagentur client is tested with a fake
`HttpMessageHandler` and hand-written fixtures (`tests/JobAutopilot.Tests/Fixtures/arbeitsagentur/`), the endpoint
with a fake `IVacancySource`. Without Docker the integration tests are skipped locally (in CI they fail instead).
To use an existing PostgreSQL server instead of a container — a temporary database is created and dropped on it:

```powershell
$env:JOBAUTOPILOT_TEST_POSTGRES = "Host=localhost;Port=5432;Username=jobautopilot;Password=jobautopilot"
dotnet test backend/JobAutopilot.sln
```

## Repository layout

```
backend/   JobAutopilot.sln, src/{Api,Core,Infrastructure}, tests/JobAutopilot.Tests
frontend/  Nuxt 4 app: app/pages (list, detail), app/components, app/composables/useVacanciesApi.ts,
           app/utils (URL filters, dates, URL safety — unit-tested in frontend/test)
docs/      roadmap, architecture decisions, API notes
prompts/   task prompts, one per roadmap step
scripts/   backup-db.ps1, restore-db.ps1
dev.ps1    dev mode in one window
```

# job-autopilot

[![CI](https://github.com/NetStall/Autopilot/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/NetStall/Autopilot/actions/workflows/ci.yml)

A personal tool for a structured job search in Germany. It imports vacancies from the Bundesagentur für Arbeit and
Himalayas, filters and ranks them against my profile with rules and a local model (Ollama), and prepares complete
application packages — cover letter and CV with the Claude API, plus the e-mail or form texts — only from facts in
my profile. **The tool never sends anything:** I review and approve every application, send it myself and only record
that it was sent; follow-ups and statistics close the loop.

| Vacancy list | Detail with „Passt zu mir?" | „Heute bereit" |
|---|---|---|
| ![List](docs/img/demo-list.png) | ![Detail](docs/img/demo-detail.png) | ![Heute bereit](docs/img/demo-heute-bereit.png) |

<sub>Screenshots from a separate demo stack with fictional companies and the invented example profile
(`docs/profile.example.json`) — no real data.</sub>

## How it works

A layered pipeline: cheap rules first, the local model only for what the rules let through, Claude only for the few
vacancies I prepare, and a human at the end. Diagram and data flows: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md);
where is what in the code: [docs/CODEMAP.md](docs/CODEMAP.md).

1. **Import** (Arbeitsagentur, Himalayas, or pasted by hand) with details, de-duplication and a title pre-filter.
2. **Hard filters**: role type, stack, explicit German C1, reachability — hidden with the reason, never a vacancy
   with an application or my „Passt".
3. **Rules**: relevance, fit check against the profile (must vs. plus, alternatives, German level, years, region,
   employer type) → rank.
4. **Local model** (`qwen3:8b`): requirement extraction and a „KI-Bewertung" next to the rank for the Top 100.
5. **Application channel**: e-mail address, portal-only, form, contact person — only shown, never contacted.
6. **Bewerbungspaket** with Claude, checked by a fact guard; review, approval, sending by me; follow-ups after 7 days.

## Quick start (Windows)

Prerequisites: [Docker Desktop](https://www.docker.com/products/docker-desktop/) running; optional
[Ollama](https://ollama.com) for the local model; optional Claude API key for letters.

```powershell
Copy-Item .env.example .env            # set POSTGRES_PASSWORD; optional CLAUDE_API_KEY=… and OLLAMA_ENABLED=true
Copy-Item docs/profile.example.json data/profile.json   # then replace everything with your own data (git-ignored)
ollama pull qwen3:8b                   # optional: local requirement extraction and KI-Bewertung
docker compose up -d                   # → http://localhost:3000 (API http://localhost:5080)
```

Dev mode (`./dev.ps1`: db in Docker, API with `dotnet watch`, Nuxt dev server) needs the .NET 10 SDK and Node 24.
Without a Claude key everything works except generating letters; without Ollama the rules decide alone.

## My daily routine

```powershell
./scripts/daily-run.ps1                # import → filters → scoring → local KI → channels → summary (≈ 5 min)
```

Then in the UI: **Heute bereit** → „Top 5 vorbereiten" (cost shown first) → open a package → „Unterlagen prüfen" →
Freigeben → send it myself (copy the e-mail text / mailto, attachments as ZIP, or the portal checklist and copy blocks)
→ „Als gesendet markieren". Follow-ups that are due appear on the same page with a ready e-mail / LinkedIn text.

## Security & privacy

- **Human approval:** nothing is ever sent by the tool — no SMTP, no „send all", no schedule.
- **No invented facts:** letters and package texts use only profile facts; a fact guard flags technologies, numbers
  and language levels that are not in the profile.
- **No scraping:** only public job APIs; detected addresses are shown, never contacted; no requests to company sites.
- **Secrets and personal data stay local:** API keys via user-secrets / `.env`, `data/` (profile, PDFs) git-ignored and
  mounted read-only into the API container, logs contain ids only; all ports bound to 127.0.0.1.
- **Untrusted input:** posting text is data — it is delimited and neutralised in every prompt, model answers are
  validated against the text (e.g. an e-mail address the model names must occur in the posting).
  Threat model and review: [docs/SECURITY.md](docs/SECURITY.md).

## Evaluation

Ranking quality on my gold labels (`./scripts/eval-ranking.ps1`, latest run 2026-10-22, 14 explicit labels — a trend,
not a benchmark; reports in [docs/evaluation/](docs/evaluation/)):

| Variant | P@10 | P@20 | NDCG@20 |
|---|---|---|---|
| Nur Regeln | 0.70 | 0.57 | 0.99 |
| **Kombiniert (rules + extraction, default)** | **0.80** | 0.57 | **1.00** |
| Nur KI-Bewertung (fitscore.v3) | 0.80 | 0.57 | 0.96 |
| With 3 automatic labels: Kombiniert | 0.90 | 0.59 | 1.00 |

With 8 „Passt" among 14 labels, 0.80 is the best possible P@10.

## Ideas for later

Embeddings with pgvector („Ähnliche Stellen", semantic search) · more sources · sending e-mail via SMTP — only after
authentication and with the same per-application approval.

---

The sections below describe each feature in detail.

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

Dependencies point inwards: `Api → Infrastructure → Core`. The layered pipeline as a diagram:
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#the-pipeline-layers-15).
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

> **JSON from PowerShell:** Windows PowerShell 5.1's `ConvertTo-Json` turns a one-element array into a plain value
> (`places: [ { … } ]` becomes `places: { … }`, and the API answers 400). Use PowerShell 7 (`pwsh`), write the JSON as a
> string (as in the examples below), or wrap arrays: `ConvertTo-Json -InputObject @($items) -Depth 10`.

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
| `skills[]` | name, `aliases` (spellings in job ads), `level`: `Production` (at work) · `Project` (own projects) · `Learning` · `None` (a gap), optional `years`, `group`, `cvGroup` (heading in the CV: `Backend`, `Frontend`, `Testing`, `DevOps & Tools` …) |
| `knownGaps[]` | missing skill + an honest phrasing in `de` **and** `en` |
| `projects[]`, `education[]`, `languages[]` (`A1`…`C2`, `Native`), `certificates[]` | |
| German texts (optional) | for German CVs and letters: `summary["fullstack.de"]`, `titleDe`, `locationDe`, bullet `textDe`, `descriptionDe`, `degreeDe`, `institutionDe`, `notesDe`, language `nameDe`/`noteDe`. Missing ones fall back to the base text (the review shows how many) |
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
| Bewertung | default Mindestbewertung (40 in a fresh database), N of „Nur Top N" and an optional default for „Deutsch erforderlich" for the list; fit penalties (0–50) |
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
phrasing from `knownGaps`).

- **Matching:** a skill counts only by its name or a specific alias. Generic aliases such as „CI/CD", „Unit Tests"
  or „Container" are never evidence for GitHub Actions, Vitest or Docker; the Profil page warns about them. The
  reasons show the text that matched, e.g. „Git („GitLab“) ✓".
- **How important a requirement is** comes from the posting's sections and markers:
  - *must-have*: headings like „Was du mitbringst", „Dein Profil", „Anforderungen", „Requirements", or
    „(Must-have)" in the sentence;
  - *nice to have*: „Ein Plus, aber kein Muss", „Wünschenswert", or „von Vorteil" / „idealerweise" in the
    sentence;
  - otherwise *required*;
  - *no penalty* when it is offered as an alternative to a skill I cover („Angular oder Vue.js", „z. B. MSSQL,
    PostgreSQL, MySQL").
- **Warnings:**
  - German level above mine (see *Deutsch erforderlich* below);
  - more years than mine — „Senior" in the title alone is a weak signal; „langjährige Erfahrung", „5+ Jahre",
    „Lead" or Senior inside the requirements is a strong one;
  - not fully remote outside the Suchprofil's places (fully remote jobs never get this penalty). On site only or a
    stated share of at most 60 % home office (the Arbeitsagentur field „Homeoffice (bis zu 40 %)", else the text:
    „2 Tage Homeoffice") makes the job *practically not reachable* — a bigger penalty, hidden from the list while
    „Nicht erreichbare Stellen ausblenden" is on (list filter „Nicht erreichbare anzeigen", `includeUnreachable=true`).
    An unknown share stays visible with a moderate penalty. A bare „Homeoffice möglich" flag never outranks a text cue:
    „teilweise im Homeoffice", „hybrides Arbeiten im Headquarter" or „2 Tage pro Woche vor Ort" make it partial
    (unreachable outside my places). The badge says what was used („Homeoffice: bis 40 % (Feld)" / „teilweise (laut
    Text)"), and „Warum?" names a conflict between field and text;
  - an employer type set to „niedriger einordnen";
  - a missed Remote-Präferenz.
- **Default order:** the *rank* = relevance minus these penalties. The penalties are the Suchprofil's „Fit-Abzüge",
  seeded from `Fit:Penalties`:

  | Penalty | Points |
  |---|---|
  | German, per level above mine | 8 |
  | Experience, weak signal | 8 |
  | Experience, strong signal | 10 |
  | Missing must-have | 15 each, at most 30 |
  | Missing in the tech stack („Sprachen & Technologien") | 10 each |
  | Other required gap | 6 each |
  | Missing „Plus" | 4 each |
  | All gaps except must-haves together, at most | 18 |
  | Outside the region | 15 |
  | Outside the region, home-office share unknown | 20 |
  | Practically not reachable (instead of „outside the region") | 35 |
  | Employer type | 10 |
  | Remote wish | 10 |

- **Technologies I don't have** never score: a Suchprofil keyword that names a known technology counts only when
  the profile has it (`Production`/`Project` fully, `Learning` half). The catalog
  `backend/src/JobAutopilot.Core/Fit/technologies.json` (184 frameworks, UI libraries, ORMs, protocols, cloud
  services, domain terms) turns every technology the profile does not list into a gap where it is asked for. Line
  markers („(Pflicht)", „zwingend", „Voraussetzung", „mindestens", „required") make a line a must-have whatever its
  section. Matches only under a nice-to-have heading count half; basics (HTML/CSS, Git, Scrum, REST, Docker …)
  together at most 6 (`Relevance:BasicSkills`). Decision: [ADR 0012](docs/decisions/0012-technology-catalog-and-reachability.md).
- **Regression safety:** real postings in `backend/tests/JobAutopilot.Tests/Fixtures/vacancies/` are ranked in
  `RankingRegressionTests`, which fails when a rule change moves one of them out of its range.

**Deutsch erforderlich.** Every vacancy gets the German level it asks for:

| Value | Detected from |
|---|---|
| *C1 oder höher* | C1/C2, „verhandlungssicher", „sehr gute Deutschkenntnisse", „fließend Deutsch", „Muttersprache Deutsch" |
| *B2* | B2, „gute Deutschkenntnisse", „sicher auf Deutsch" |
| *B1 oder niedriger* | A1–B1, „Grundkenntnisse", „Deutschkenntnisse von Vorteil" |
| *Kein Deutsch nötig* | „German is not required", „English is our working language", or an English posting without any German requirement |
| *Unbekannt* | nothing found |

- The strongest finding wins; a level next to another language („Englisch C1, Deutsch B2") does not count for German.
- The reasons name the phrase.
- **List filter** „Deutsch erforderlich", with counts, in the URL as `german=B2,B1OrLower`. An optional default for
  it lives in the Suchprofil.
- **Page Statistik** shows the distribution for the relevant vacancies (Passt + Vielleicht, rank ≥
  Mindestbewertung), overall and by remote level and source.

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

### Unterlagen (cover letter, CV, review)

On a vacancy, **Unterlagen erstellen** creates an application in status *Entwurf* and, within about a minute:

1. **Anschreiben** — drafted by Claude (one call) from *facts only*.
   - The facts are numbered items from `profile.json`: matching experience bullets, skills with level, known gaps
     with my honest phrasing, languages and links. Salary and earliest start from the Suchprofil are added only if
     the posting asks for them.
   - Language (German / English), „du"/„Sie" and the contact person („z. Hd. Frau …") are detected from the posting.
   - Style: one page, 4–5 short paragraphs, my website in the first one, a „Ganz offen: …" paragraph for the gaps,
     no clichés.
   - Prompt: `backend/src/JobAutopilot.Infrastructure/Claude/Prompts/letter.v2.md` (v1 kept), versioned; the version is
     stored with each draft.
2. **Lebenslauf** — built without AI: the summary chosen by focus (full stack / backend / AI), a role line, bullets
   ordered by overlap with the posting, skill groups. Always one page.
3. **Faktenprüfung** — rules that run after every generation and save. They warn about:
   - technologies claimed as experience but not in the profile (or only `Learning`/`None`);
   - numbers or durations that appear in neither the profile nor the posting;
   - required gaps that are not mentioned;
   - „Sie" and „du" mixed, the wrong language;
   - more than one page.

   Hints: website missing, clichés.

The review screen (`/unterlagen/{id}`, also via **Bewerbungen → Details → Unterlagen**) has:
- **Editing:** every paragraph as a text area, with **Absatz neu formulieren** (one call), a character and page
  indicator, and warnings next to their paragraph. CV: focus, show/hide and reorder bullets and skill groups.
- **Live preview:** PNG of page 1 of both documents.
- **Spickzettel** for application forms, with copy buttons: subject, a short e-mail text, salary, earliest start,
  „Wie haben Sie uns gefunden?", German level, links, contact person.
- **Verlauf:** every generation and save is a version; older versions can be restored.

**Freigeben** writes the PDFs to `data/applications/{applicationId}/`, e.g.
`Vitalii_Strashuk_Anschreiben_<Firma>.pdf` and `…_Lebenslauf_<Firma>.pdf`; the download buttons appear. With open
warnings, approving needs a ticked confirmation, which is recorded in the history. A change after approval sets the
application back to *Entwurf*.

**Als gesendet markieren** (channel + date) is the only way to *Gesendet*, and only after the approval. The tool
never sends anything; I submit the documents myself.

The Claude API key comes only from user-secrets or the environment:

```powershell
dotnet user-secrets set "Claude:ApiKey" "<key>" --project backend/src/JobAutopilot.Api   # dev mode
# Docker: CLAUDE_API_KEY=<key> in .env (git-ignored)
```

Without a key the API runs; documents cannot be generated (503 with a clear message).

To try it on the best vacancies of the list (one Claude call each; prints time, tokens, cost, warnings and the letter
to the console only). It skips vacancies with an application in any status, hidden or unreachable ones, and goes on
until N letters are drafted:

```powershell
./scripts/draft-top-vacancies.ps1 -Count 3
```

The prompt is `letter.v2` (06c: salutation with the company, a concrete hook, at most three achievements, only real
gaps, a fixed closing; `Claude:LetterPromptVersion = letter.v1` for comparison). The FactGuard accepts a technology
named in an honest gap sentence anywhere in that paragraph and checks languages against the profile's levels.

**German CV texts.** The German CV falls back to English where a `*De` field is missing. `./scripts/translate-profile.ps1`
drafts the missing ones with the letter model (one call), shows each for approval and only then writes
`data/profile.json` (backup in `backups/`; the API itself never writes the profile — `data/` is mounted read-only).

| `Claude` setting | Default | |
|---|---|---|
| `MaxTokens` | 8000 | per call, thinking included |
| `Effort` | `medium` | `low` … `max`; empty = model default |
| `Temperature` | empty | only sent when set — current models reject non-default values |
| `TimeoutSeconds` / `MaxRetries` | 90 / 2 | per attempt; 429/5xx are retried with backoff |
| `DailyCallLimit` | 30 | generations + rephrasings per day (German time) → 429 |
| `DraftTokenBudget` | 120000 | tokens per draft → 429 |
| `InputPricePerMTok` / `OutputPricePerMTok` | 2 / 10 | USD, only for the cost shown per draft |

The model is set only in `Llm:Tasks` (`"Letter": "claude:claude-sonnet-5-5"`), see
[ADR 0013](docs/decisions/0013-llm-abstraction.md).

### Local model (Ollama)

Optional. Without Ollama everything works as before: the AI features fall back to the rules. Personal data and
postings stay on the PC.

1. Install Ollama for Windows natively from [ollama.com/download](https://ollama.com/download). It runs in the
   background and listens on `http://localhost:11434`.
2. Give the model room for long German postings, then restart Ollama from the tray:

   ```powershell
   setx OLLAMA_CONTEXT_LENGTH 16384
   ```

3. Pull the models. The default `qwen3:8b` (q4_K_M, 5.2 GB) leaves room for a 16K context in 12 GB VRAM. The
   optional `qwen3:14b` (q4_K_M, 9.3 GB) is for comparison and is tight with a long context.

   ```powershell
   ollama pull qwen3:8b
   ollama pull qwen3:14b
   ```

4. Switch it on.
   - Dev mode: `dotnet user-secrets set "Llm:Providers:Ollama:Enabled" "true" --project backend/src/JobAutopilot.Api`,
     or set it in `appsettings.Development.json`.
   - Docker: `OLLAMA_ENABLED=true` in `.env`. The API reaches the host as `http://host.docker.internal:11434`.
5. Check it.
   - `Invoke-RestMethod http://localhost:5080/api/llm/status` shows whether Ollama is reachable, the pulled models
     and which tasks are ready.
   - `ollama ps` shows the loaded model and `100% GPU` when it runs on the graphics card.
   - `nvidia-smi` shows the VRAM in use.

| `Llm:…` | Default | Meaning |
|---|---|---|
| `Tasks:Letter` | `claude:claude-sonnet-5-5` | provider:model for the cover letter |
| `Tasks:Extraction` | `ollama:qwen3:8b` | provider:model for the requirement extraction; `ollama:qwen3:14b` to compare |
| `Providers:Ollama:Enabled` | `false` | off = no call to Ollama, rules only |
| `Providers:Ollama:BaseUrl` | `http://localhost:11434` | from Docker `http://host.docker.internal:11434` |
| `Providers:Ollama:TimeoutSeconds` / `MaxRetries` | 180 / 1 | per attempt; 429/5xx/connection errors are retried |
| `Extraction:AutoAfterImport` | `true` | analyse new/changed vacancies after each import (only when Ollama is on) |
| `Extraction:MaxPerRun` | 500 | upper limit of „Top N" (Statistik → KI-Analyse: Top 50 / 100 / 200 / 500, default 100) |
| `Extraction:AutoTopN` | 100 | after an import only new or changed vacancies within the current Top N |
| `Extraction:MaxOutputTokens` | 1500 | output cap per answer — a repetition loop ends early; a timeout fails only that vacancy |
| `Extraction:PromptVersion` | `extraction.v3` | v1 and v2 stay for comparison; part of the cache key |
| `Extraction:Thinking` | `false` | `reasoning_effort: none` — a thinking model answers directly (much faster) |

Switch the extraction model in Docker with one line in `.env`: `Llm__Tasks__Extraction=ollama:qwen3.5:9b`. Everything
after the first colon is the model with its tag, so `ollama:qwen3:14b` works too.

**Requirement extraction.** With Ollama on, every vacancy with a description gets its requirements read by the model
in the background, one at a time and most relevant first. The model reads must-have and nice-to-have, German and
English level, years, seniority, home office, employment type, required domains and a two-sentence summary. The
detail page shows them under „Anforderungen" with the source („KI" or „Regeln"), and „Mit KI analysieren" analyses
one vacancy right away. Statistik → „KI-Analyse" starts or cancels a full run and shows s/vacancy and tokens/s.

- The result is cached per text, model and prompt version, so nothing is sent twice.
- An invalid answer is retried once, then the rules stay in charge.
- A required domain I don't have („Broadcast/MAM") is a must-have gap. The company's industry is not one.
- `extraction.v2` and the guards behind it: must-have only when the text marks it as required, one list per item,
  only concrete items (generic practices dropped; „automatisierte Qualitätssicherung" → my testing skill), years only
  from a stated number. Missing methods (DDD, CQRS, Event Sourcing …) cost like a plus and share a cap of 6.
- Decision: [ADR 0014](docs/decisions/0014-local-llm-extraction.md).

**KI-Bewertung.** With the task `Llm:Tasks:FitScore` (default `ollama:qwen3:8b`), the same background queue also asks
the model for its own judgement of each vacancy: score 0–100, verdict („passt" / „eher nicht" / „passt nicht"),
three reasons and the blockers. The model gets my profile (skills by level, gaps in my own phrasing, languages,
places, remote wish, employer preference — no name or contact data). The list shows „KI 72" next to the rank, „Warum?"
the reasons. It does **not** change the rank. Cached per text, profile, model and prompt. Prompt `fitscore.v2` (07c)
checks location, employer type, German and must-haves first; its caps (not reachable ≤ 40, service provider ≤ 60) are
enforced in code.

**Sort.** „Sortierung" in the list: *Kombiniert* (rank = rules + extraction, default), *Nur Regeln* (the rank without
any AI input), *Nur KI* (KI-Bewertung, vacancies without one last), plus newest / last imported (`sort=combined|rules|ai|published|fetched`).

**Gold labels.** „Passt" / „Passt nicht" store my own judgement: on the detail page, or in the list with `p` / `n` on
the focused card (`j` / `k` move; the same key again removes it). Automatic labels need no click: applied
(„Gesendet" or later) → Passt, „Ausblenden" → Passt nicht; my own label always wins. The header shows
„Bewertet: 12 + 9 automatisch / 30". `./scripts/seed-gold-labels.ps1` sets the labels already given in chat.

**Evaluation.** `./scripts/eval-ranking.ps1` prints precision@10/20 and NDCG@20 of every ranking variant over the
labels, with and without the automatic ones; `-Model qwen3.5:9b` first lets that model analyse the labelled
vacancies (real local calls, cached; the vacancies do not change). Reports: [docs/evaluation/](docs/evaluation/).

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
| GET | `/api/vacancies` | paged `{ items, page, pageSize, totalCount, totalMatching, top, facets: { source, remoteLevel, employerType, relevanceLevel, requiredGerman } }`, filters below; each facet counts under all *other* filters |
| GET | `/api/vacancies/{id}` | one vacancy, 404 if unknown |
| GET | `/api/vacancies/{id}/fit` | „Passt zu mir?": `{ profileStatus, hasDescription, requirements: [{ name, coverage: Covered\|Partial\|Gap, optional, note }], warnings: [{ text, penalty }], requiredGerman, requiredYears, penalty }` |
| GET | `/api/applications?status=&channel=&followUpDue=true` | applications, newest first, with `statusLabel`, `followUpAt`, `isFollowUpDue` |
| GET | `/api/applications/{id}` | one application with its status history (`events`) |
| POST | `/api/applications` | record an application by hand → 201 (status *Gesendet*) |
| PATCH | `/api/applications/{id}` | edit fields (not the status) |
| DELETE | `/api/applications/{id}` | delete → 204 |
| POST | `/api/applications/{id}/status` | `{ status, note?, at? }` → history event, last action = now; *Entwurf*, *Freigegeben* and *Gesendet* cannot be set here (409) — a draft can only be withdrawn |
| POST | `/api/vacancies/{id}/apply` | „Als beworben markieren" → 201, or 200 with the existing application |
| POST | `/api/applications/import-tracker` | import `data/import/bewerbungen-export.json` (idempotent) → report; 400 if the file is missing/invalid |
| GET | `/api/profile` | `{ status: Loaded\|Missing\|Invalid, fileName, errors, warnings, experienceYears, profile }` |
| POST | `/api/vacancies/{id}/documents` | „Unterlagen erstellen" → **201** with the documents (new application in *Entwurf*), **200** if they exist; 409 if the vacancy's application is already sent; 422 without a profile; 429 at a limit; 503/504/502 when Claude is not configured / times out / fails — nothing is stored then |
| POST | `/api/applications/{id}/documents` | the same for an existing *Entwurf* / *Freigegeben* application |
| GET | `/api/applications/{id}/documents` | letter, CV options, built CV, warnings, pages, usage and cost, cheat sheet, revisions |
| PUT | `/api/applications/{id}/documents` | `{ version, letter, cvOptions }` → saved + checked again; 409 on an old `version` |
| POST | `/api/applications/{id}/documents/regenerate` | `{ version }` → new letter (one call) |
| POST | `/api/applications/{id}/documents/paragraphs/{index}/rephrase` | `{ version, hint? }` → that paragraph rewritten (one call) |
| POST | `/api/applications/{id}/documents/restore` | `{ version, revision }` → an older version as a new one |
| POST | `/api/applications/{id}/documents/preview/{letter\|cv}` | `{ letter?, cvOptions? }` (unsaved state) → `image/png` of page 1, header `X-Page-Count` |
| POST | `/api/applications/{id}/documents/approve` | `{ version, acceptWarnings }` → *Freigegeben*, PDFs written; 409 with open warnings unless `acceptWarnings` |
| GET | `/api/applications/{id}/documents/{letter\|cv}.pdf` | the approved PDF (404 before approval) |
| POST | `/api/applications/{id}/mark-sent` | `{ channel, at? }` → *Gesendet* (only after approval; 409 otherwise) |
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
| GET | `/api/statistics/german` | `{ minScore, total, overall: [{ level, label, count, percent }], byRemoteLevel: [{ key, total, levels }], bySource: [...] }` for the relevant vacancies |
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
| `german` | `C1Plus`, `B2`, `B1OrLower`, `None`, `Unknown` — repeatable or comma-separated (`german=B2,B1OrLower`) |
| `search` | name of a configured search — repeatable, matches any |
| `interest` | `None`, `Starred`, `Dismissed` — repeatable; without it, dismissed vacancies are hidden |
| `includeDismissed` | `true` = show dismissed vacancies too |
| `includeUnreachable` | `true` = show practically unreachable vacancies although the Suchprofil hides them |
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
cd frontend; npm test; npm run typecheck      # Vitest: logic in Node + component tests in a Nuxt environment (happy-dom)
```

The Claude client is tested against scripted HTTP responses (success, 429 with retry, 5xx, 401, timeout, cut-off and
invalid answers, a prompt-injection posting); the API tests use a fake `ILetterGenerator`. No test calls Claude.

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

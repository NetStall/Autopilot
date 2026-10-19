# ADR 0008 — Second vacancy source: Himalayas remote jobs API

Status: accepted (2026-10-19, step 04 part D)

## Context

The Bundesagentur für Arbeit finds few *fully remote* jobs. Step 04 asks for one additional `IVacancySource` with
remote jobs open to applicants in Germany, from an API whose terms clearly allow programmatic use — **no scraping**
of LinkedIn, StepStone, Indeed or other sites whose terms forbid it.

## Candidates (checked 2026-10-19: documentation, terms, one sample request each)

| | Arbeitnow | Jobicy | Remotive | **Himalayas** |
|---|---|---|---|---|
| API | `GET /api/job-board-api`, 300/page, no search | `GET /api/v2/remote-jobs`, `geo`, `tag`, last 7 days only | `GET /api/remote-jobs`, `search` | `GET /jobs/api/search`: `q`, `country`, `exclude_worldwide`, `sort`, `page` (20/page) |
| Terms | "free public API, please do not abuse … you agree to the terms of service on arbeitnow.com" — no API-specific terms found | allowed for "websites, applications …"; keep Jobicy as source, canonical URL | "do not submit Remotive jobs to third-party sites"; ≤ 4 requests/day, > 2/min blocked; jobs delayed 24 h | clause 113: "personal and commercial integrations" allowed with conditions (below) |
| Attribution | "appreciate linking back" | "credit Jobicy and link to the source" | link back + name Remotive | visible credit + link to Himalayas; apply via `applicationLink` |
| Rate limits | not stated | ≤ 1 sync pass/hour | 4/day | documented (keyed 60/min, 10 000/day; unkeyed share a public allowance), 429 + `Retry-After` |
| Germany / remote | Germany-focused, but only 29 of 300 remote, no keyword search | `geo=germany`: 17 of 136 tagged Germany, 3 relevant dev titles | 12 hits for `.NET`, none restricted to Germany | `country=DE&exclude_worldwide=true`: `.NET` 64, `C#` 52, `Vue` 44, `Full Stack` 165, `Softwareentwickler` 12 — many German postings |
| Remote field | `remote` bool | all remote, `jobGeo` | all remote, `candidate_required_location` | all remote, `locationRestrictions[]` (countries) |

## Decision

**Himalayas.** It is the only candidate with explicit API terms that allow personal integrations, a documented rate
limit and error model, a keyword search, and a country filter that returns relevant German C#/.NET/Vue jobs.
Arbeitnow is Germany-focused but has no search and no clear API terms; Jobicy has few German developer jobs and only
7 days; Remotive's limits (4/day, 24 h delay) and coverage do not fit.

## How the terms are respected

- **Apply link:** `Vacancy.Url` = the API's `applicationLink` (the „Öffnen" button uses it).
- **Attribution:** every Himalayas vacancy shows „via Himalayas" with a link to `https://himalayas.app` (card and
  detail page, „Quelle").
- **No redistribution:** the data stays in the local database of one user; nothing is published or forwarded.
- **Caching and expiry:** one import per day (scheduler) or on demand; `expiryDate` is stored as `ExpiresAt`;
  expired vacancies are hidden immediately and deleted after the next import.
- **Rate limits:** sequential pages, 1.5 s pause, at most 5 pages (100 jobs) per search, 5 searches → ≤ 25
  requests per import; 429/5xx are retried with backoff by the standard resilience handler (respects `Retry-After`).
  No API key is needed; an optional key (`Himalayas:ApiKey`, user-secrets/environment only) is sent as Bearer token.
- Descriptions arrive as HTML and are converted to plain text before storing; the UI never renders HTML.

## Consequences

- `VacancySearch.Source` selects the source; Himalayas searches have no location (always "remote, open to DE").
- A source that fails skips only its own searches (recorded per search with an error); the run fails only when every
  search failed — the Arbeitsagentur import no longer depends on a second service being up.
- Cross-source duplicates: `DuplicateKey` = company + title, lower-case, without gender markers and legal forms.
  The list shows each job once (the one imported first) with „auch bei …" links to the others. This also merges
  reposts of the same job within one source.
- New column `ExpiresAt` (beyond the approved schema list) — needed to comply with the expiry rule.

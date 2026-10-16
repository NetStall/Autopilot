# ADR 0002 — Vacancy import (step 02)

Status: accepted · 2026-10-16

## Manual trigger first

An import runs only when `POST /api/imports/arbeitsagentur` is called. A scheduler would add hosting concerns
(background service, overlapping runs, where it runs on Azure) before we even know how often new postings appear
and how the API reacts to regular traffic. A manual trigger is enough to fill the list, keeps the load on a public,
undocumented service predictable, and the endpoint is exactly what a scheduler will call later.

## Upsert that never touches user state

Vacancies are matched by `Source + ExternalId`. A known vacancy gets fresh *posting* data (title, company,
location, url, published date) because employers edit postings. Everything that reflects what the user or a later
step decided — `IsSeen`, `EmployerType` (step 04), `Description`, `FetchedAt` (first time we saw it) — is kept.
Otherwise every import would bring already reviewed postings back as "new" and undo classifications.

The whole run is saved with **one** `SaveChanges`: if the source fails halfway, nothing is written and the
endpoint returns 502, so there are no half-imported runs to reason about.

## No detail fetch yet

The search result already has everything the list needs (reference number, title, company, place, date, link).
Fetching `jobdetails` per posting would multiply the requests to the public API by up to 250 per search and is only
needed once letters are generated from the full description (step 06). It will be added there, for the vacancies
the user actually picks.

## Smaller decisions

- `VacancyImportService` lives in Core and works against `IVacancySource` and `IVacancyStore`, so the upsert rules
  are unit-tested without HTTP or a database. Per-search counts are returned in `ImportResult.Searches` and logged
  by the API (Core has no logging dependency).
- `Fetched` counts every result the source returned, including duplicates within the run;
  `Added + Updated` count distinct vacancies. A known vacancy counts as updated even if nothing changed.
- The Jobsuche endpoint is `pc/v6/jobs`; `pc/v4/jobs` answers 403 (see [arbeitsagentur-api.md](../arbeitsagentur-api.md)).
- Missing `firma` becomes "Unbekannter Arbeitgeber"; overlong values are truncated to the column limits instead of
  failing the whole import.
- All `DateTimeOffset` values are normalised to UTC when written (EF value converter), because Npgsql rejects
  other offsets for `timestamptz`. No schema change.
- An empty `POST` body means "run the configured searches" whatever the `Content-Type` is, because Windows
  PowerShell's `Invoke-RestMethod -Method Post` sends `application/x-www-form-urlencoded` without a body.

# ADR 0009 — Editable Suchprofil in the database

Status: accepted (2026-10-20, step 05b)

## Context

Until step 05, *what I am looking for* was spread over three places: the import searches and title exclusions in
`appsettings.json`, the keyword lists in `Relevance`, and `preferences` (regions, remote wish, employer types to avoid)
in `data/profile.json`. Changing a search term meant editing JSON and restarting the API. Step 05b asks for one
editable **Suchprofil** in the UI, while `profile.json` stays the read-only source of *who I am* (the facts that
letters may use).

## Decision

- **Separation.**
  - `profile.json` = facts about me (skills, experience, languages, gaps). It is read-only for the app.
  - Suchprofil = preferences, stored in the database and edited on the page **Suchprofil**.
  - `appsettings.json` keeps only seed defaults and the scoring *mechanics*: group caps, base score, title factor,
    softened factor, level thresholds, remote/employer phrases.
- **Storage: one row as jsonb.**
  - Table `SearchProfiles`: `Id` = 1, `Document` jsonb, `Version` as the optimistic-concurrency token, `UpdatedAt`.
  - Table `SearchProfileRevisions`: one row per saved version (document plus a German change summary), keeping the
    last 50. A rollback saves an old document as a *new* version, so history is never rewritten.
  - **Why jsonb and not normalised tables:**
    - the profile is always read and written as a whole;
    - it has no relations and is never queried by its parts;
    - it changes shape as the product grows.

    A versioned document keeps that cheap. Validation lives in Core (`SearchProfileRules.Validate`), not in the
    database.
- **Concurrency.**
  - `PUT /api/search-profile` takes the version that was loaded.
  - A different current version, or a lost race in the database, returns **409**. The UI then offers „Neu laden"
    rather than overwriting.
- **Effects of a save.**
  - The in-memory snapshot is replaced.
  - The scorer and the fit analysis are rebuilt, cached by (profile version, Suchprofil version).
  - A background re-score is queued.
  - The import searches are **generated** from search terms × places / remote × enabled sources
    (`SearchProfileRules.GenerateSearches`). They are shown read-only.
  - New terms or places only take effect at the next import, so the UI offers „Jetzt importieren". Nothing is
    imported automatically on save.
- **Preview.**
  - `POST /api/search-profile/preview` validates a draft, then scores all stored vacancies in memory with it
    (AsNoTracking, nothing saved).
  - It returns the matching count under the draft's Mindestbewertung, including how many are fully remote.
  - The UI calls it debounced (600 ms).
- **Migration from `profile.json`.**
  - On the first start with an empty table, the Suchprofil is seeded from the old config:
    - `Import:Searches` → search terms and places;
    - `Relevance` positive rules → Must-have (weight ≥ 8) and Nice-to-have;
    - `Relevance` negative rules → Ausschluss;
    - `Fit:Penalties` → penalties.
  - It is also seeded **once** from `profile.json` `preferences`:
    - regions → additional places;
    - „Hybrid" → *mindestens Hybrid*;
    - avoided employer types → *niedriger einordnen*;
    - salary, commute and earliest start.
  - After that, `preferences` in `profile.json` are ignored. The API logs a hint while they are still in the file.
    The fallback reader is kept for one version and then removed.

## Consequences

- One migration (`AddSearchProfile`). The Suchprofil survives restarts and is included in database backups.
- Search names are generated, for example `region-softwareentwickler-csharp` and `remote-dotnet`. Some differ from
  the names of the old configured searches, so old values in `MatchedSearches` no longer appear in the „Suche"
  filter until the next import refreshes them.
- Saving re-scores while the import lock is held. A scheduled or manual import therefore waits for the lock
  (`Import:LockWaitSeconds`, default 120 s) instead of failing.
- Export and import are plain JSON of the document. An imported file is validated through the preview endpoint and
  then loaded into the form as unsaved changes. Saving it is a normal, versioned save.

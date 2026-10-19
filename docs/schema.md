# Schema notes

PostgreSQL via EF Core; migrations in `backend/src/JobAutopilot.Infrastructure/Persistence/Migrations`. Enums are
stored as strings. Only what is not obvious from the entity classes is noted here.

## Vacancies

| Column | Notes |
|---|---|
| `Source` + `ExternalId` | unique; `Manual` vacancies get a generated id |
| `Description` | plain text (Himalayas HTML is converted on import, Arbeitsagentur Markdown escapes removed), ≤ 20 000 chars |
| `DetailsFetchedAt` | when the description was fetched; `null` = to fetch. Cleared when the source reports a newer change date |
| `RemoteHint` | the **sticky** source signal: Arbeitsagentur `homeofficeprozent`/`homeofficemoeglich`, Himalayas (= remote), found by a `RemoteOnly` search (≥ Hybrid). Only ever raised |
| `RemoteLevel` | **recomputed** on every scoring: an explicit phrase in title/description wins, „kein Homeoffice" cannot undo a hint ≥ Hybrid, otherwise the hint |
| `RelevanceScore`, `RelevanceLevel`, `RelevanceReasons` (text[]) | from the relevance rules; recomputed by re-scoring; `null` = not scored yet (counts as „Vielleicht") |
| `EmployerType` | from Arbeitsagentur flags / known agencies / phrases; for manual vacancies the user's choice |
| `DuplicateKey` | company + title normalised (no gender markers, no legal forms) — the same job from several sources is listed once |
| `ExpiresAt` | Himalayas `expiryDate`. **Required by the Himalayas API terms** (remove jobs once they expire, ADR 0008): expired vacancies are hidden at once and deleted after the next import. `null` for the Arbeitsagentur |
| `MatchedSearches` (text[]) | names of the searches that found the vacancy |
| `Interest`, `IsSeen` | user triage; imports never change them |

## ImportRuns

One row per import; a filtered unique index on `IsActive` allows only one queued/running import (ADR 0005/0006).
`Searches` is a jsonb column with the per-search counts (incl. source and error).

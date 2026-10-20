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
| `EmployerType` | from Arbeitsagentur flags / known agencies / phrases; Himalayas (lists the hiring company) = `Direct` unless agency signals are found; for manual vacancies the user's choice |
| `Unreachable` | `true` = practically not reachable (outside my places, not fully remote, at most 60 % home office or unknown); recomputed on every scoring; hidden from the list while the Suchprofil says so (step 05d) |
| `RequiredGerman` | `C1Plus` / `B2` / `B1OrLower` / `None` / `Unknown` — the German the posting asks for (filter „Deutsch erforderlich"), recomputed on every scoring; indexed. Rows from before step 05c are `Unknown` until the next re-scoring (runs at start-up) |
| `DuplicateKey` | company + title normalised (no gender markers, no legal forms) — the same job from several sources is listed once |
| `ExpiresAt` | Himalayas `expiryDate`. **Required by the Himalayas API terms** (remove jobs once they expire, ADR 0008): expired vacancies are hidden at once and deleted after the next import. `null` for the Arbeitsagentur |
| `MatchedSearches` (text[]) | names of the searches that found the vacancy |
| `Interest`, `IsSeen` | user triage; imports never change them |

## SearchProfiles, SearchProfileRevisions

The Suchprofil ([ADR 0009](decisions/0009-search-profile.md)): **one row** in `SearchProfiles` (`Id` = 1) with the
whole profile as jsonb `Document` (camelCase, enums as strings). `Version` is the concurrency token — a save with an
outdated version gets 409. `SearchProfileRevisions` holds one row per saved version (`Version` unique, `Document`,
German `Summary` of the change, `SavedAt`); only the last 50 are kept. A rollback writes an old document as a new
version. Seeded on first start from `appsettings.json` (+ once `preferences` from `profile.json`).

## ApplicationDrafts, ApplicationDraftRevisions

The documents of one application (step 06, [ADR 0010](decisions/0010-application-documents.md)) — personal data.

| Column | Notes |
|---|---|
| `ApplicationId` | unique (one draft per application); deleting the application deletes the draft (cascade) and the API deletes its PDF folder |
| `Letter` (jsonb) | `{ subject, salutation, paragraphs[], closing, language, formality, factsUsed[], gapsMentioned[] }` — plain text |
| `CvOptions` (jsonb) | my CV choices: `focus`, `hiddenBullets`, `shownBullets`, `bulletOrder`, `hiddenSkillGroups`, `skillGroupOrder` (ids like `exp0.b3`) |
| `Warnings` (jsonb) | fact guard + CV checks of the current state: `[{ code, message, paragraph, severity: Warning\|Hint }]` |
| `PromptVersion`, `Model` | of the last generation (e.g. `letter.v1`, `claude-sonnet-5-5`) |
| `InputTokens`, `OutputTokens`, `Calls` | summed over all model calls of this draft (budget `Claude:DraftTokenBudget`) |
| `Version` | concurrency token — a save with an older version gets 409 |
| `ApprovedAt`, `WarningsAccepted` | set by „Freigeben"; cleared by any later change |
| `LetterFile`, `CvFile` | file names of the PDFs in `data/applications/{applicationId}/` |

`ApplicationDraftRevisions`: one row per state (`Kind`: `Generated`, `Rephrased`, `Saved`, `Restored`) with letter,
CV options and — for model calls — model, prompt version and tokens; `(DraftId, Number)` unique. Restoring copies an
old revision into a new one. The daily call limit counts `Generated` + `Rephrased` rows since midnight (German time).

## ImportRuns

One row per import; a filtered unique index on `IsActive` allows only one queued/running import (ADR 0005/0006).
`Searches` is a jsonb column with the per-search counts (incl. source and error).

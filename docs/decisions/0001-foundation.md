# ADR 0001 — Foundation choices (step 01)

Status: accepted · 2026-10-13

## Minimal APIs grouped per feature (not controllers)

Each feature has one `*Endpoints.cs` file with a `MapGroup` (`/api/vacancies` …), next to its DTOs and mappings
(`Features/Vacancies/`). It keeps endpoints small and explicit, `TypedResults` give an accurate OpenAPI document,
and it is the direction ASP.NET Core is investing in. Request bodies are validated with the built-in
.NET 10 `AddValidation()` (DataAnnotations → `ValidationProblemDetails`), so no extra validation library is needed.

## Integration tests against real PostgreSQL via Testcontainers

The API is tested in memory with `WebApplicationFactory`, but against a real PostgreSQL 17 container instead of
the EF in-memory provider or SQLite. Unique indexes, string-stored enums, `timestamptz` and migrations behave
exactly as in production, and nothing has to be installed besides Docker.

- `JOBAUTOPILOT_TEST_POSTGRES=<connection string>` uses an existing server instead of a container.
  A temporary database (`jobautopilot_tests_<guid>`) is created and dropped, so real data is never touched.
- Without Docker (and without that variable) the integration tests are **skipped** locally, so unit tests still run.
  In CI (`CI=true`) they **fail** instead, so a broken setup cannot hide.
- Tests run as `Testing` environment; the fixture applies migrations itself (auto-migration is Development-only).

## Smaller decisions

- `GET /api/vacancies` returns `{ items, page, pageSize, totalCount }` so the UI can page; `page < 1` or
  `pageSize` outside 1–100 is a 400 ProblemDetails (not silently clamped).
- "Newest first" = by `FetchedAt` (when the vacancy entered the system), then by `Id`.
  `PublishedAt` is optional and missing for many manual entries; sorting options can come with step 03.
- `GET /api/vacancies/{id}` exists so `POST` can return a real `Location` header.
- Manual vacancies get `Id = Guid v7` and `ExternalId = manual-<id>`; the rule lives in `Vacancy.CreateManual` (Core).
- Field length limits live in `Core/Vacancies/VacancyLimits.cs` and are shared by validation and the EF schema.
- Enums are serialized as strings in JSON (`"employerType": "Agency"`) as well as in the database.
- `appsettings.Development.json` contains a connection string for the **local Docker** database only
  (same defaults as `.env.example`). It is not a secret; anything real goes into user-secrets or environment variables.
- xUnit v3 runs on Microsoft.Testing.Platform (`global.json`), FluentAssertions is pinned to 7.x
  (Apache-2.0; 8.x requires a commercial licence).

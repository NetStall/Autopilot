# Task 01 — Project foundation

Read `AGENTS.md` and `docs/ROADMAP.md` first. This task is **step 01 only**. Do not implement the Arbeitsagentur client, Claude integration, sending or any UI beyond what is listed here.

## Goal

A clean, runnable skeleton that builds, tests and runs locally on Windows, with one real domain entity stored in PostgreSQL. Everything later builds on this, so prefer clarity over cleverness.

## 1. Backend solution (`backend/`)

- Create `JobAutopilot.sln` with:
  - `src/JobAutopilot.Api` — ASP.NET Core Web API (.NET 10), controllers **or** minimal API endpoints grouped per feature (pick one, explain why in one line in the README).
  - `src/JobAutopilot.Core` — class library.
  - `src/JobAutopilot.Infrastructure` — class library.
  - `tests/JobAutopilot.Tests` — xUnit + FluentAssertions.
- References: `Api → Infrastructure → Core`, `Tests → Core, Infrastructure, Api`.
- Enable `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` via a shared `Directory.Build.props`.
- Add `.editorconfig` with standard C# conventions.

## 2. Domain (Core)

Create:

- `enum EmployerType { Unknown, Direct, Agency, Temporary }`
- `enum VacancySource { Arbeitsagentur, Manual }`
- `class Vacancy`:
  - `Guid Id`
  - `VacancySource Source`, `string ExternalId` (unique together)
  - `string Title`, `string Company`, `string? Location`, `string? Url`
  - `string? Description`
  - `EmployerType EmployerType`
  - `DateTimeOffset? PublishedAt`, `DateTimeOffset FetchedAt`
  - `bool IsSeen`

Keep it a plain entity — no EF attributes in Core.

## 3. Infrastructure

- `AppDbContext` with `DbSet<Vacancy>`, configuration in a separate `IEntityTypeConfiguration<Vacancy>` (unique index on `Source + ExternalId`, max lengths, enums stored as strings).
- Npgsql provider. Connection string from configuration key `ConnectionStrings:Default`.
- An `AddInfrastructure(this IServiceCollection, IConfiguration)` extension method.
- First migration: `InitialCreate`.

## 4. API

- `GET /health` → 200 with `{ "status": "ok" }` plus a DB connectivity check (use ASP.NET Core health checks).
- `GET /api/vacancies` → list of `VacancyDto` (newest first). Paging via `page` and `pageSize` (default 20, max 100).
- `POST /api/vacancies` → create a **manual** vacancy (Source = Manual, ExternalId generated). Validate required fields, return ProblemDetails on errors.
- OpenAPI document in Development, CORS allowing the Nuxt dev origin (`http://localhost:3000`) from configuration.
- Apply migrations automatically **only in Development**.

## 5. Tests

- Unit test for DTO mapping or validation.
- Integration tests with `WebApplicationFactory` for `/health`, `GET /api/vacancies` and `POST /api/vacancies`.
  Use Testcontainers for PostgreSQL if Docker is available; explain the choice in the README.

## 6. Frontend (`frontend/`)

- New Nuxt 4 app, TypeScript strict, SCSS.
- Runtime config `public.apiBase` (env `NUXT_PUBLIC_API_BASE`, default `http://localhost:5080`).
- One page `/` titled "Job Autopilot" that loads `GET /api/vacancies` and shows a simple list (title, company, location, employer type) with loading, empty and error states.
- No UI library. Minimal, clean styles.

## 7. Infrastructure files

- `docker-compose.yml` with a `db` service (postgres:17, named volume, healthcheck). Credentials from a `.env` file; commit only `.env.example`.
- Root `.gitignore` covering .NET, Node/Nuxt, `.env`, `appsettings.*.local.json`, and `data/` (for future CVs/letters).
- `CLAUDE.md` containing only: `@AGENTS.md`.

## 8. CI

`.github/workflows/ci.yml` on push and pull request:
- job `backend`: setup .NET 10, restore, build (Release), test.
- job `frontend`: setup Node 24, `npm ci`, `npm run build`.

## 9. README

Short and useful for a reviewer: what the project is (2–3 sentences), stack, architecture diagram in text, how to run locally (PowerShell commands), how to run tests, current status (step 01 of the roadmap).

## Done when

- `docker compose up -d db`, `dotnet build`, `dotnet test`, `dotnet run` and `npm run dev` all work on Windows.
- Opening `http://localhost:3000` shows the empty state; after a `POST /api/vacancies` the vacancy appears.
- CI is green.
- You report: the plan you followed, files created, commands to verify, and any decision you made that I should review.

Commit in logical steps (backend skeleton → domain + EF → API → tests → frontend → CI/docs).

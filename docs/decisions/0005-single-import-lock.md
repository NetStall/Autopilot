# ADR 0005 — Only one import at a time (PostgreSQL advisory lock)

Status: accepted · 2026-10-18

## Context

Two imports running at the same time can both decide that a vacancy is new and both insert it — the second
`SaveChanges` then fails on the unique index (`Source + ExternalId`) with a 500, after minutes of work. It also
doubles the load on the public Arbeitsagentur API. Imports can start from the UI button, from a second browser tab,
from `Invoke-RestMethod`, and soon from the daily schedule (task 03c, part C). The API may run twice against the same
database (`dev.ps1` and `docker compose`), and later on more than one Azure instance.

## Decision

A **PostgreSQL session-level advisory lock** (`pg_try_advisory_lock` with a fixed application key) on a dedicated
connection, behind the Core interface `IImportLock`. A second start does not wait: it gets **409 Conflict** with a
ProblemDetails message.

- Works across processes and instances — an in-process `SemaphoreSlim` would not.
- No schema change and no "running" flag that can get stuck: if the process crashes, PostgreSQL drops the session
  and with it the lock.
- `TryAcquire` (no waiting) gives the user immediate feedback instead of a hanging request.
- The unique index stays as the last line of defence.

## Alternatives

- **In-process lock** — simplest, but wrong as soon as two API processes share the database.
- **Lock row / status column** — needs a migration and cleanup after crashes (stale "running" rows).
- **Serializable transactions** — the import fetches for minutes before it writes; a long transaction is worse.

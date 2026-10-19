# ADR 0006 — Background import queue

Status: accepted · 2026-10-19

## Context

An import of 15 searches takes seconds to minutes (sequential paging with pauses, retries). As one blocking HTTP
request it gave no feedback, could be cut by proxies/timeouts, and a closed tab aborted it. The daily schedule
(ADR 0007) needs to start imports without a request at all.

## Decision

- `POST /api/imports` creates an `ImportRuns` row (`Queued`) and returns **202** with `{ importId }` at once.
- An in-process `Channel<ImportJob>` feeds a single `BackgroundService` (`ImportWorker`) that runs one job at a
  time and writes status, current search, searches done/total and the counts into the run row.
- The UI polls `GET /api/imports/{id}` every second; `GET /api/imports?limit=10` lists recent runs.
- No new package (no Hangfire/Quartz): a channel and a hosted service are enough for one user and one process.

## Consequences and guards

- **One import at a time** (also ADR 0005): a filtered unique index allows only one *active* run (`Queued` or
  `Running`) — a second start gets 409; while running, the worker also holds the PostgreSQL advisory lock, so a
  second API process against the same database cannot run in parallel either.
- **Restarts:** the channel lives in memory. Runs left active by a process that died are marked `Failed`
  ("Interrupted …") when the next process starts — before it accepts requests, and only if no other process holds
  the lock.
- **Shutdown:** the worker's token is cancelled; the import stops before its single `SaveChanges`, so nothing is
  half-written, and the run is stored as `Cancelled`.
- **Progress granularity** is per search (one DB write each), not per page — cheap and good enough for a progress
  bar over ~15 searches.
- The blocking `POST /api/imports/arbeitsagentur` was removed instead of kept as a second code path.

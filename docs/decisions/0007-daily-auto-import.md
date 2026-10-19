# ADR 0007 — Daily automatic import

Status: accepted · 2026-10-19

## Decision

`Import:Schedule` (`Enabled`, `TimeOfDay` = `07:00`, `TimeZone` = `Europe/Berlin`) starts one import per day through
the **same** `ImportCoordinator`, queue and guards as the button (ADR 0006, 0005).

- **Fetching only.** The scheduler can only queue imports. Nothing that sends data anywhere (applications, e-mails)
  is ever scheduled — AGENTS.md: sending always needs an explicit human approval.
- **Enabled in docker compose** (the "use" mode that runs all the time), off by default and in tests; dev mode
  leaves it off so `dotnet watch` restarts do not trigger imports.
- **Local time, DST-aware:** `DailySchedule` computes the next 07:00 in Europe/Berlin from `TimeProvider`, so a
  day with a clock change is 23 or 25 hours long and the import still runs at 07:00 local time. A time inside the
  spring-forward gap runs right after the gap; an ambiguous time on fall-back runs once (first occurrence).
  Unit-tested for 29 March and 25 October 2026.
- **Catch-up:** a laptop is often off at 07:00. At start-up the scheduler checks whether the latest import was
  requested before the most recent 07:00 slot and, if so, queues one immediately (any manual import after 07:00
  counts). It does not replay several missed days.
- If an import is already active at 07:00, the scheduled one is skipped (logged), not queued behind it.

## Alternatives

- Windows Task Scheduler / cron calling the API — another moving part outside the app and the compose file.
- Quartz.NET / Hangfire — new dependencies for a single daily job.

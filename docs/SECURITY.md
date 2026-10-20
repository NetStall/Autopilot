# Security

Status: 2026-10-18 (task 03c, part B). Re-check before anything is reachable from outside localhost.

## Hard rule

> **Authentication is required before any part of the app is reachable from anywhere other than localhost —
> and before step 08 (sending applications) is built.** This includes Azure (step 10), a LAN, a tunnel or a
> reverse proxy. Until then the API has no authentication *on purpose* and must stay bound to `127.0.0.1`.

## What runs where

| Component | Where | Reachable from | Data |
|---|---|---|---|
| PostgreSQL 17 (`db`) | Docker, volume `pgdata` | `127.0.0.1:5432` | vacancies (public job postings), seen/interest state, **applications** (personal data: contacts, notes, salary), **letters and CV choices** (step 06) |
| API (`api`, ASP.NET Core) | Docker or `dotnet watch` | `127.0.0.1:5080` | reads/writes the DB; calls the Bundesagentur für Arbeit Jobsuche API and the Himalayas jobs API (outbound HTTPS, read-only, no credentials; an optional Himalayas key only via user-secrets/environment) |
| Frontend (`web`, Nuxt/Nitro) | Docker or `nuxt dev` | `127.0.0.1:3000` | no data of its own; the browser calls the API directly |
| Backups | `./backups/*.sql` | local disk, git-ignored | full DB dump |
| Approved PDFs | `./data/applications/{id}/` | local disk, git-ignored; the only folder the API writes to | Anschreiben + Lebenslauf (personal data) |
| Claude API (step 06) | Anthropic, outbound HTTPS | — | per generation: the selected profile facts + the posting. The key comes only from user-secrets / environment, is never sent to the frontend or logged |

Nothing listens on a non-loopback interface (compose binds every port to `127.0.0.1`; verified: the API does not
answer on the machine's LAN address). Containers run as non-root users (`app`, UID 1654 / `node`, UID 1000).

## Threat model (local, single user)

| Threat | Mitigation |
|---|---|
| Another device on the network reads or changes data | Ports bound to `127.0.0.1` only |
| A malicious website in the user's browser calls the local API (CSRF / cross-origin reads) | CORS allows exactly the configured frontend origin (no wildcard, validated at start); JSON bodies require `Content-Type: application/json`, which triggers a preflight |
| DNS rebinding (attacker domain resolving to 127.0.0.1) | Host filtering: `AllowedHosts = localhost;127.0.0.1;[::1]` → other `Host` headers get 400 |
| XSS through vacancy data (titles, URLs, descriptions come from external APIs) | Himalayas HTML descriptions are converted to plain text before storing (tags removed, entities decoded, tested with `<script>`/`onerror`); Arbeitsagentur descriptions are plain text; Vue escapes all text, no `v-html`; links only for absolute `http(s)` URLs with `rel="noopener noreferrer"`; CSP without `unsafe-inline` for scripts (hash-based), `connect-src` limited to the API origin, `frame-ancestors 'none'` |
| Clickjacking | `X-Frame-Options: DENY`, `frame-ancestors 'none'` (frontend and API) |
| Abuse of the import (floods the public Arbeitsagentur API, DB contention) | Only one import at a time (unique active run + advisory lock → 409), rate limit 6 starts/min (429), sequential paging with delay; the daily schedule only *fetches* and never sends anything |
| Oversized or malformed input | Validation with limits on every parameter (text ≤ 200, page ≤ 1 000 000, pageSize ≤ 100, lists bounded), request bodies ≤ 64 KB (413), malformed JSON → 400 ProblemDetails |
| Information leakage in errors | ProblemDetails everywhere; outside Development no exception messages or stack traces (tested) |
| Secrets / personal data in git | `.env`, `appsettings.*.local.json`, `backups/`, `data/` (profile, tracker export) git-ignored; history checked (see findings) |
| Personal data in logs or external calls (step 05) | Profile and applications are never logged — only file name, status, counts and ids; `./data` is mounted read-only into the API container (only `./data/applications` is writable, for the PDFs); profile/application endpoints are localhost-only like everything else |
| Personal data sent to the Claude API (step 06) | Only on an explicit click (create / regenerate / rephrase), only the facts selected for that letter plus the posting; logs hold ids, model, prompt version, tokens, duration, status codes — never letter, profile, posting or the API's error body; daily call limit and per-draft token budget |
| Prompt injection through a posting („ignore previous instructions …") | The posting is passed as data in a `<vacancy>` block with `<`/`>` replaced (cannot close or forge blocks), the system prompt forbids following instructions from it; structured JSON output; the answer is plain text, escaped by Vue (no `v-html`), PDFs render text only; tested with an injection fixture |
| Sending without approval | Not possible: the tool has no sending at all; „Gesendet" is a record reachable only via „Als gesendet markieren" after „Freigeben", and any later change revokes the approval |
| Vulnerable dependencies / insecure code | CI fails on high/critical (NuGet + npm, documented exceptions), Dependabot weekly, CodeQL (fails on findings with security-severity >= 7; results go to the Security tab once code scanning is available — public repository or GitHub Code Security — otherwise they are kept as a SARIF artifact) |

Not in scope while localhost-only: authentication, TLS, encryption at rest (the Docker volume is protected by the
OS user account), multi-user separation.

## Decisions

- **No authentication yet** — localhost-only, single user. See the hard rule above.
- **Plain HTTP locally, no HSTS.** Browser and API are on the same machine; HSTS on `localhost` would affect every
  other local project. In Azure (step 10) TLS terminates at the platform ingress; HTTPS redirection and HSTS are
  switched on there together with authentication.
- **Local dev credentials** (`jobautopilot`/`jobautopilot`) are in `appsettings.Development.json` and
  `.env.example`. They only protect a database bound to `127.0.0.1` and must never be used elsewhere. Real secrets
  (Claude API key, SMTP) will only come from user-secrets / environment variables / Key Vault.
- **Rate limit and lock both:** the lock prevents parallel runs (also across processes), the rate limit prevents
  many sequential runs.
- **CSP via a Nitro plugin**, static headers via `routeRules`: the CSP needs the runtime API URL and the hashes of
  the page's inline scripts.

## Findings of the review (2026-10-18)

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | PostgreSQL published on `0.0.0.0:5432` (whole network) with a well-known dev password | high | **fixed** — all ports on `127.0.0.1` |
| 2 | npm: 2 critical (`simple-git`) and 12 high transitive advisories | high | **fixed** — lockfile updated (`npm audit fix`, no breaking changes) |
| 3 | npm: `node-forge` (GHSA-86w9-cpqp-85rv) and `braces` (GHSA-vfj7-8cjw-p6xm) high, no patched release exists | medium | **documented** — build/dev tooling only, not in the production bundle (checked in CI); time-boxed allowlist until 2027-01-18 |
| 4 | No authentication | high once exposed | **documented** — hard rule; acceptable while localhost-only |
| 5 | DNS rebinding possible (`AllowedHosts: *`) | medium | **fixed** — host filtering |
| 6 | Two imports at the same time could fail on the unique index (500) or double the load | medium | **fixed** — PostgreSQL advisory lock, 409 |
| 7 | Import could be triggered without limit (outbound requests to a public API) | medium | **fixed** — rate limit 6/min, 429 with `Retry-After` |
| 8 | No security headers / CSP on frontend and API | medium | **fixed** — headers on both, hash-based CSP (verified in the browser without violations) |
| 9 | CORS allowed any header and method; a wildcard origin in config would have been accepted | low | **fixed** — GET/POST/PATCH (+ DELETE since step 05 for deleting an application, + PUT since step 06 — the Suchprofil save of 05b used PUT and was blocked by the browser), `Content-Type`, exposed `X-Page-Count`, origins validated at start |
| 10 | Default request body limit 30 MB | low | **fixed** — 64 KB, 413 ProblemDetails |
| 11 | `employerType` list unbounded | low | **fixed** — max 10 values |
| 12 | Migrations bound to the environment name | low | **fixed** — `Database:MigrateOnStartup` |
| 13 | No retry on transient DB errors, default timeouts | low | **fixed** — `EnableRetryOnFailure` (3×), command timeout 30 s, shutdown timeout 30 s |
| 14 | One `/health` endpoint for liveness and readiness | low | **fixed** — `/health/live`, `/health/ready` (compose uses ready) |
| 15 | Docker base images not pinned (`postgres:17`) | low | **fixed** — all pinned (SDK 10.0.402, ASP.NET 10.0.13, node 24.22.0-alpine3.24, postgres 17.11-trixie) |
| 16 | Exception details in error responses outside Development | — | **verified OK** — test added |
| 17 | Secrets in git history | — | **verified OK** — none; only the local dev password (see decisions) |
| 18 | Logging of personal data / full external responses | — | **verified OK** — only ids, counts, search names; HTTP client logs at Warning; EF sensitive data logging off; Arbeitsagentur failures log status, not bodies |
| 19 | Containers as root | — | **fixed in part A** — non-root users |
| 20 | Plain HTTP, no HSTS | low | **deferred** to step 10 (TLS at the Azure ingress), see decisions |
| 21 | Personal data at rest unencrypted (Docker volume, backups) | low | **documented** — protected by the OS account; backups git-ignored |
| 22 | DB password visible via `docker inspect` (environment variable) | low | **documented** — local only; Key Vault / managed identity in step 10 |

## Checklist before going beyond localhost (step 10)

1. Authentication (e.g. Entra ID) on API and frontend; no anonymous endpoints except `/health/*`.
2. TLS everywhere, HSTS, HTTPS redirection; `AllowedHosts` and CORS set to the real domain.
3. Secrets from Key Vault / managed identity, no passwords in environment variables.
4. Database not publicly reachable (private endpoint / firewall).
5. Re-run this review; remove expired allowlist entries.

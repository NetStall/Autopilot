# ADR 0003 — Vacancy list UI (step 03)

Status: accepted · 2026-10-18

## Filters live in the URL

The list's state (search text, place, employer types, status, sort, page) is the URL query. Reload, back/forward,
bookmarks and the detail page's back link all work without client-side state. Text input is debounced (300 ms)
and uses `router.replace` so typing does not fill the history; every other change uses `router.push`.
Parsing and writing the query are pure functions (`app/utils/vacancyFilters.ts`) and unit-tested.

## Client-side data loading

Lists and details load in the browser (`useAsyncData`, `server: false`). The app is a personal tool talking to a
local API; server rendering would only add a second network path (Nitro → API) with its own base URL and errors.
While filters change, the previous result stays visible (dimmed) instead of flashing the skeleton; the skeleton
is shown only before the first response.

## Seen state

Toggling "seen" updates the card immediately and rolls back if the request fails. Opening the original posting
marks it as seen (click and middle-click). The bulk action marks the unseen vacancies of the current page and then
reloads the page, because with the "Neu" filter those items leave the list.

## No new index

`q` and `location` are substring searches (`ILIKE '%…%'`), which a B-tree index cannot serve; a trigram (GIN)
index would need the `pg_trgm` extension. With hundreds to a few thousand vacancies a sequential scan is fast, so no
index (and no schema change) was added. Revisit with `pg_trgm` if the list grows beyond ~50 000 rows.

## Smaller decisions

- `employerType`, `source` and `sort` are bound as strings and validated by name (case-insensitive), so unknown or
  numeric values (`employerType=7`) become a 400 naming the parameter instead of a silent match on an undefined
  enum value. Validation error keys use the C# property names (`EmployerType`, `PageSize`).
- `PATCH /api/vacancies/{id}` rejects any field other than `isSeen` (`JsonUnmappedMemberHandling.Disallow`).
- An import counts a known vacancy as `updated` only if a stored field changed, otherwise as `unchanged`.
- External links from vacancy data are rendered only for absolute `http:`/`https:` URLs
  (`safeExternalUrl`), always with `target="_blank" rel="noopener noreferrer"`; vacancy text is never rendered as HTML.
- Frontend tests cover the logic (URL ↔ filters, dates, URL safety, list states, import summary) as pure functions.
  Mounting components would additionally need a DOM implementation (`happy-dom` or `jsdom`), which is not an
  approved dependency yet; the UI flows were checked in a real browser instead.

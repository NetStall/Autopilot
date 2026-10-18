# Bundesagentur für Arbeit — Jobsuche API

The public search API behind the official job board app. It is **not officially documented**; the community
documentation is <https://github.com/bundesAPI/jobsuche-api>. Everything below was checked against the live
service on **2026-10-16** (7 manual requests) and **2026-10-18** (home office, Germany-wide search, keyword
counts — about 70 requests with 1 s pause); re-check when something breaks.

## Endpoint

```
GET https://rest.arbeitsagentur.de/jobboerse/jobsuche-service/pc/v6/jobs
X-API-Key: jobboerse-jobsuche
User-Agent: job-autopilot/0.2 (+https://github.com/NetStall/Autopilot)
```

`jobboerse-jobsuche` is the public client id of the official app, not a secret. No OAuth/token flow is needed.

```powershell
Invoke-RestMethod -Headers @{ 'X-API-Key' = 'jobboerse-jobsuche' } `
  'https://rest.arbeitsagentur.de/jobboerse/jobsuche-service/pc/v6/jobs?was=Softwareentwickler&wo=Regensburg&umkreis=30&angebotsart=1&veroeffentlichtseit=14&page=1&size=5'
```

### Query parameters we send

| Parameter | Meaning | Our value |
|---|---|---|
| `was` | keyword (URL-encoded: `C#` → `C%23`) | `VacancySearch.Keyword` |
| `wo` | place; **omitted = all of Germany** | `VacancySearch.Location` |
| `umkreis` | radius in km around `wo` | `VacancySearch.RadiusKm` (only together with `wo`) |
| `homeoffice` | home-office filter, see below | `Arbeitsagentur:RemoteHomeOfficeFilter` when `RemoteOnly` |
| `angebotsart` | offer type, `1` = Arbeit (employment) | always `1` |
| `veroeffentlichtseit` | published within the last n days (0–100) | `VacancySearch.PublishedWithinDays` (default 30) |
| `page` | 1-based page | 1 … `MaxPages` |
| `size` | page size (≤ 100) | `Arbeitsagentur:PageSize` (default 100) |

Also available but not used yet: `arbeitszeit` (`vz;tz;snw;mj` — **`ho` no longer works in v6**, see below),
`zeitarbeit` (`true`/`false`), `pav` (private placement agencies), `befristung`, `arbeitgeber`, `berufsfeld`.
Unknown parameter names are silently ignored; a known parameter with an unknown value returns `400`
(`EINGABEN_UNVOLLSTAENDIG_ODER_FEHLERHAFT`, `"detail": "Wert ungültig"`).

### Home office / remote (verified 2026-10-18)

The old `arbeitszeit=ho` (Heim-/Telearbeit) is still accepted but **returns 0 results** in v6 for every keyword.
Home office is a separate parameter `homeoffice` (found in the official web app, which maps old `arbeitszeit=ho`
links to it). Values, semicolon-separated, **order matters** (`nv_true;prozentual_0` → 400):

| `homeoffice=` | Meaning | `Softwareentwickler`, Germany, 30 days |
|---|---|---|
| — | no filter | 3413 |
| `prozentual_100` | posting states 100 % home office (fully remote) | 32 |
| `prozentual_50` | states ≥ 50 % | 78 |
| `prozentual_0` | states any percentage | 124 |
| `prozentual_0;nv_true` | any percentage **or** "nach Vereinbarung" — the web app's "Homeoffice" filter | 663 |
| `prozentual_100;nv_true` | fully remote or by arrangement | 571 |

`nv_true` ("by arrangement") is mostly hybrid work at the employer's site. For "remote anywhere in Germany" we
therefore default to `prozentual_100` (`Arbeitsagentur:RemoteHomeOfficeFilter`); widen it in configuration if
too few results arrive. Results also carry `homeofficemoeglich` (bool) and, when stated, `homeofficeprozent`.

### Result counts per keyword (2026-10-18, `angebotsart=1`, `veroeffentlichtseit=30`)

| Keyword (`was`) | `wo=Deggendorf&umkreis=70` | Germany, no filter | Germany, `prozentual_100` | Germany, `prozentual_0;nv_true` |
|---|---|---|---|---|
| `Softwareentwickler` | 55 | 3413 | 32 | 663 |
| `C%23` (C#) | 39 | 2257 | 3 | 175 |
| `.NET` | 51 | 2478 | 3 | 172 |
| `Full%20Stack` | 9 | 555 | 7 | 170 |
| `Fullstack` | 11 | — | 5 | 113 |
| `Webentwickler` | 1 | 66 | 4 | 22 |
| `Backend` | 10 | 990 | 17 | 260 |
| `Anwendungsentwickler` | 4 | 322 | 29 | 92 |
| `Vue` | 11 | 193 | 5 | 41 |

- `C#` must be sent as `C%23`: an unencoded `#` starts the URL fragment and the API would search for `C`.
  The client uses `Uri.EscapeDataString` (covered by a test).
- **Bare `C#` and `.NET` are imprecise in a region.** The search is fuzzy: `C%23` also matches driving licence
  class "C/CE" (truck drivers), `.NET` matches sales, butchers, care workers … Share of IT titles among the results
  (region Deggendorf + 70 km): `C#` 17/39, `.NET` 10/51 — but `Softwareentwickler C#` 7/7, `C# .NET` 5/5,
  `.NET Entwickler` 4/4, `C# Entwickler` 4/4. Together with `homeoffice=prozentual_100` the bare keywords are
  precise (3/3 each), because the home-office filter narrows the results a lot.
- `dotnet` and `ASP.NET` return (almost) nothing; `Full Stack` and `Fullstack` return partly different postings.

### Final search list (2026-10-18)

A full import with the default searches: 183 results, **117 distinct vacancies** (77 region, 40 fully remote),
0 excluded; a second run reports 117 unchanged. With step 02's configuration the import found 3.

| Name | Keyword | Results | New in the run | Why |
|---|---|---|---|---|
| `region-softwareentwickler` | Softwareentwickler | 55 | 55 | broad, 53/55 IT titles |
| `region-csharp` | Softwareentwickler C# | 7 | 0 | precise; marks C# postings (filter „Suche") |
| `region-csharp-dotnet` | C# .NET | 5 | 3 | precise replacement for bare `C#` |
| `region-dotnet` | .NET Entwickler | 4 | 1 | precise replacement for bare `.NET` |
| `region-fullstack` / `-alt` | Full Stack / Fullstack | 9 / 11 | 4 / 2 | both spellings find different postings |
| `region-anwendungsentw` | Anwendungsentwickler | 4 | 2 | |
| `region-backend` | Backend | 10 | 5 | |
| `region-vue` | Vue | 11 | 5 | |
| `remote-csharp` / `remote-dotnet` | C# / .NET | 3 / 3 | 3 / 3 | fully remote, Germany |
| `remote-fullstack` | Full Stack | 7 | 6 | |
| `remote-vue` | Vue | 5 | 1 | |
| `remote-softwareentwickler` | Softwareentwickler | 32 | 22 | broad (also Java, SAP …) — scoring in step 04 |
| `remote-backend` | Backend | 17 | 5 | |

Dropped: `Webentwickler` (region: 1 result, already found by other searches); bare `C#` / `.NET` in the region
(noise, see above).
- `angebotsart=1` already excludes apprenticeships (`4`), internships/trainee programmes (`34`) and
  self-employment (`2`); titles can still contain such terms (e.g. working students), hence the title
  pre-filter at import (`Import:ExcludeTitleTerms`).

### Response fields we use

```jsonc
{
  "ergebnisliste": [                       // missing entirely when there are no results
    {
      "referenznummer": "10000-1234567890-S",   // → Vacancy.ExternalId
      "stellenangebotsTitel": "…",              // → Title
      "firma": "…",                             // → Company ("Unbekannter Arbeitgeber" if missing)
      "stellenlokationen": [ { "adresse": { "plz": "93055", "ort": "Regensburg", "region": "…", "land": "…" } } ],
      "veroeffentlichungszeitraum": { "von": "2026-10-09" },   // → PublishedAt (midnight Europe/Berlin)
      "datumErsteVeroeffentlichung": "2026-10-09",             //   fallback for PublishedAt
      "externeURL": "https://…"                 // optional; → Url if absolute https
    }
  ],
  "maxErgebnisse": 7,
  "page": 1,
  "size": 5
}
```

- `Location` = `"{plz} {ort}"` of the **first** `stellenlokationen` entry; `plz` is often `null`.
- `Url` = `externeURL` when it is an absolute `https` URL, otherwise the public detail page
  `https://www.arbeitsagentur.de/jobsuche/jobdetail/{referenznummer}` (URL-encoded; verified: 200).
- Entries without `referenznummer` or `stellenangebotsTitel` are skipped and counted (log at Debug).
- Other fields (`hauptberuf`, `arbeitszeit*`, `homeofficemoeglich`, `verguetungsangabe`, `gehaltsspanneVon/Bis`,
  `entfernung`, `chiffrenummer`, `arbeitgeberKundennummerHash`, …) are ignored for now.
- Text is UTF-8 (umlauts arrive correctly).

## Differences from the task description / older documentation

| Expected (prompt 02, bundesAPI examples) | Reality on 2026-10-16 |
|---|---|
| `GET pc/v4/jobs` | **403 Forbidden** (also `pc/v4/app/jobs`). Only `pc/v6/jobs` answers. |
| results in `stellenangebote[]` | results in **`ergebnisliste[]`**, and the field is **absent** when there are 0 results |
| `refnr`, `titel`, `arbeitgeber` | `referenznummer`, `stellenangebotsTitel`, `firma` |
| `arbeitsort { plz, ort, … }` | `stellenlokationen[].adresse { plz, ort, region, land }` |
| `aktuelleVeroeffentlichungsdatum` | `veroeffentlichungszeitraum.von` (+ `datumErsteVeroeffentlichung`) |
| `externeUrl` | `externeURL` (capital `URL`) |
| — | unchanged: header `X-API-Key`, all query parameters, `maxErgebnisse`/`page`/`size`, detail page URL |

## Paging and etiquette

- Pages are requested **sequentially** with `DelayBetweenPagesMs` (default 300 ms) between them.
- Stop when a page is empty, when `page * size >= maxErgebnisse`, or after `MaxPages` (default 10, per search
  overridable). With the defaults one search makes at most 10 requests / 1000 results; a warning is logged when a
  search stops at `MaxPages` although `maxErgebnisse` reports more.
- Imports are triggered manually only (`POST /api/imports/arbeitsagentur`), no scheduler.
- Transient failures (5xx, 408, 429, timeouts) are retried with exponential backoff by the standard resilience
  handler (`Microsoft.Extensions.Http.Resilience`). 4xx such as 403 are not retried and make the import fail
  with 502 — nothing is saved.
- No per-posting detail requests in this step (`pc/v4/jobdetails/{base64(refnr)}` exists, see bundesAPI).
- No rate limit headers were returned; none are documented.

# Bundesagentur für Arbeit — Jobsuche API

The public search API behind the official job board app. It is **not officially documented**; the community
documentation is <https://github.com/bundesAPI/jobsuche-api>. Everything below was checked against the live
service on **2026-10-16** (7 manual requests); re-check when something breaks.

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
| `was` | keyword | `VacancySearch.Keyword` |
| `wo` | place | `VacancySearch.Location` (left out when empty) |
| `umkreis` | radius in km around `wo` | `VacancySearch.RadiusKm` (only together with `wo`) |
| `angebotsart` | offer type, `1` = Arbeit (employment) | always `1` |
| `veroeffentlichtseit` | published within the last n days (0–100) | `VacancySearch.PublishedWithinDays` |
| `page` | 1-based page | 1 … `MaxPages` |
| `size` | page size (≤ 100) | `Arbeitsagentur:PageSize` (default 50) |

Also available but not used yet: `arbeitszeit` (`vz;tz;snw;ho;mj`), `zeitarbeit` (`true`/`false`), `pav`
(private placement agencies), `befristung`, `arbeitgeber`, `berufsfeld`.

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
- Stop when a page is empty, when `page * size >= maxErgebnisse`, or after `MaxPages` (default 5).
  With the defaults one search makes at most 5 requests / 250 results.
- Imports are triggered manually only (`POST /api/imports/arbeitsagentur`), no scheduler.
- Transient failures (5xx, 408, 429, timeouts) are retried with exponential backoff by the standard resilience
  handler (`Microsoft.Extensions.Http.Resilience`). 4xx such as 403 are not retried and make the import fail
  with 502 — nothing is saved.
- No per-posting detail requests in this step (`pc/v4/jobdetails/{base64(refnr)}` exists, see bundesAPI).
- No rate limit headers were returned; none are documented.

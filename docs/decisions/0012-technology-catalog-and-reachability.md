# ADR 0012 — Technology catalog, profile-gated keywords, reachability

Status: accepted (2026-10-20, step 05d)

## Context

„Full-Stack-Entwickler – C#/.NET & Blazor" (Ing.-Büro Falk, Bergstetten, Homeoffice bis zu 40 %) ranked
**81** with relevance 100:

- „Blazor im Titel ✓": Blazor is a Suchprofil keyword, but not a skill of mine.
- DevExpress (Pflicht), XPO, SignalR and gRPC produced no gap, because only profile skills and a dozen configured
  requirements were known.
- Bergstetten is out of reach from Talstadt with 40 % home office, yet cost only the normal region penalty (15).
- Many small basics (HTML/CSS, Git, Scrum, Code Reviews, REST, Docker) added up.

## Decision

- **Keywords are gated by the profile** (`ProfileRelevanceScorerProvider.ForProfile`):
  - `Production`/`Project` count fully, `Learning` counts half (`Relevance:LearningKeywordFactor`), `None` counts
    nothing.
  - A keyword that names a catalog technology missing from the profile does not score.
  - Other keywords („Fullstack", „Softwareentwicklung") stay.
  - Blazor is removed from the shipped keyword list.
- **Technology catalog** (`Core/Fit/technologies.json`, embedded): 184 entries in 14 categories.
  - Every entry the profile does not list is a gap where it is asked for, but not when it appears only in the tasks,
    the offer or „about us".
  - An entry with a `parent` (AWS Lambda → AWS) is no second gap when the parent is already one.
- **Importance:**
  - Line markers („(Pflicht)", „zwingend", „Voraussetzung", „vorausgesetzt", „mindestens", „required", „must") make
    a line a must-have regardless of its section.
  - Stack headings („Sprachen & Technologien", „Tech-Stack", „Tooling and stack") mean *likely required*: 10 points.
  - Must-have gaps get their own cap of 30; all other gaps still share 18.
- **Positives from nice-to-have sections** count half (`Relevance:NiceToHaveFactor`).
- **Basics** (`Relevance:BasicSkills`) together count at most the `basics` cap of 6.
  - They are scaled first and still count in their own group, so this can only lower a score.
  - A separate group alone would have raised it: HTML/CSS left the already full `frontend` group.
- **Reachability:**
  - A job counts as practically not reachable when it is outside my places, not fully remote, and has at most 60 %
    home office (`HomeOfficeShare`: „bis zu 40 %", „2 Tage" = 40 %) or an unknown share.
  - Such a job costs 35 points instead of 15.
  - It is stored as `Vacancies.Unreachable` and hidden from the list while the Suchprofil option „Nicht erreichbare
    Stellen ausblenden" is on (default). `includeUnreachable=true` shows it.

## Consequences

Real profile and saved Suchprofil:

| Posting | Rank before | Rank after |
|---|---|---|
| Falk | 81 | 26 |
| Bildfluss | 42 | 11 |
| PULSAR | 36 | 35 |
| Brückenwerk .NET | 87 | 87 |
| Förderwerk | 88 | 88 |
| Lindenhof | 88 | 88 |
| Seewerk prover | 35 | 15 |
| Next.js | 7 | 0 |

- Seewerk prover („anteilig Homeoffice", Seeweiler) is now not reachable and hidden by default; its regression range
  moved to 5–25.
- **Revised in 06a:**
  - Only on site or a *stated* share of at most 60 % counts as not reachable. An unknown share costs 20 and stays
    visible, so Seewerk prover is at 30 and shown again.
  - The share comes from the Arbeitsagentur field `homeofficeprozent` first, then from the text.
  - Descriptions are fetched most relevant first. Förderwerk and Lindenhof had a description at the source; they were
    stuck behind newer vacancies in the queue (300 per run).
- The catalog is code, not configuration: a change needs a commit and a re-scoring (which runs at start-up).

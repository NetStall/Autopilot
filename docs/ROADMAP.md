# Roadmap

Each step is one prompt in `prompts/`. Finish, review and commit a step before starting the next.

| # | Step | Result |
|---|---|---|
| 01 | **Foundation** | Solution + 3 projects + tests, Nuxt app, PostgreSQL in Docker, `Vacancy` entity + first migration, `/health`, CI on GitHub Actions |
| 02 | Arbeitsagentur import | Typed client for the Jobsuche API, search by keyword/region/radius, store vacancies (dedupe by source + ref no.) |
| 03 | Vacancy list UI | Nuxt page: list, filters (region, employer type, new/seen), detail view with link to original |
| 04 | Agency detection & scoring | Rules for Personalvermittler/Zeitarbeit, simple fit score against the profile (stack keywords, region, language) |
| 05 | Candidate profile, fit check & applications | Profile in `data/profile.json` (the only source of facts for letters), rule-based fit check per vacancy, applications with status history and follow-up date, import of the existing tracker, „Beworben" badge |
| 05b | Scores in the UI, editable Suchprofil | Score badges and filters, Suchprofil in the database with history, source filter |
| 05c–05e | Scoring fixes, code map | Must-have vs. nice-to-have, required German, technology catalog, reachability; `docs/CODEMAP.md`, `docs/ARCHITECTURE.md` |
| 06 | **Application documents + review** (old 06 + 07) | **06a:** Claude drafts the Anschreiben (DE/EN) from profile facts only behind `ILlmClient` (`Llm:Tasks`), fact guard, minimal editor. **06b:** CV variant without AI, PDFs (QuestPDF), review screen with live preview, approve → PDFs, cheat sheet for forms, „Als gesendet markieren" — the tool never sends |
| 07 | Local AI | A local model (Ollama) as a second `ILlmClient` for tasks where it is good enough (e.g. rephrasing, classification), chosen per task in `Llm:Tasks` |
| 08 | Sending — *optional / later* | Manual submission with the cheat sheet covers most cases (portals, forms). E-mail sending only if it turns out to be needed — and only after authentication (docs/SECURITY.md) |
| 09 | Statistics & follow-up reminders | Reminders for due follow-ups, response rate by channel and employer type |
| 10 | Deploy to Azure | Container Apps / App Service, managed PostgreSQL, Key Vault, CI/CD |

Later ideas: more sources (company career pages, DEVjobs, GermanTechJobs).

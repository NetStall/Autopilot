# Roadmap

Each step is one prompt in `prompts/`. Finish, review and commit a step before starting the next.

| # | Step | Result |
|---|---|---|
| 01 | **Foundation** | Solution + 3 projects + tests, Nuxt app, PostgreSQL in Docker, `Vacancy` entity + first migration, `/health`, CI on GitHub Actions |
| 02 | Arbeitsagentur import | Typed client for the Jobsuche API, search by keyword/region/radius, store vacancies (dedupe by source + ref no.) |
| 03 | Vacancy list UI | Nuxt page: list, filters (region, employer type, new/seen), detail view with link to original |
| 04 | Agency detection & scoring | Rules for Personalvermittler/Zeitarbeit, simple fit score against the profile (stack keywords, region, language) |
| 05 | Candidate profile, fit check & applications | Profile in `data/profile.json` (the only source of facts for letters), rule-based fit check per vacancy, applications with status history and follow-up date, import of the existing tracker, „Beworben" badge |
| 06 | Letter generation | Claude API drafts an Anschreiben (DE/EN) from profile + vacancy; honest about gaps; saved as `Draft` |
| 07 | Review & approve | UI to edit a draft, approve it; nothing can be sent before approval |
| 08 | Sending | E-mail sending (SMTP) of approved applications with CV PDF attached; status → `Sent` |
| 09 | Statistics & follow-up reminders | Reminders for due follow-ups, response rate by channel and employer type |
| 10 | Deploy to Azure | Container Apps / App Service, managed PostgreSQL, Key Vault, CI/CD |

Later ideas: more sources (company career pages, DEVjobs, GermanTechJobs), CV variants per vacancy.

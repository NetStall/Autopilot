# ADR 0011 — Importance of requirements, generic aliases, required German

Status: accepted (2026-10-20, step 05c)

## Context

A real posting showed three weaknesses of the fit check. „Senior C# / .NET Developer – Cloud & AWS" (MEDIFOX DAN /
ResMed, Himalayas) ranked **59**, although it clearly asks for more than I bring:

- **False matches.** It got „Vitest ✓" and „GitHub Actions ✓" because my profile lists „Unit Tests" and „CI/CD" as
  aliases, and the posting uses those words.
- **No sense of importance.** AWS sits under „Was du mitbringst" but cost the same 6 points as any gap. ECS and Lambda
  sit under „Ein Plus, aber kein Muss".
- **Missed phrases.** „Kommunizierst sicher auf Deutsch" was not recognised as a German requirement, and „mehrjährige
  Erfahrung in der Senior Softwareentwicklung" weighed no more than „Senior" in the title.

## Decision

- **Generic aliases are no evidence** (`SkillTerms`).
  - A profile skill matches only by its name or a specific alias. Generic concepts never do, even when they are
    listed as aliases: CI/CD, Unit Tests, Container, Cloud, DevOps, API, Backend, …
  - This applies to the relevance bonus and to the fit check.
  - The profile validator warns about such aliases, so the profile can be cleaned up.
  - The reasons show the text that matched when it differs from the rule name, e.g. „Git („GitLab“) ✓".
- **Importance from sections** (`RequirementSections`, `RequirementImportance`).
  - Known headings start a section:
    - must-have: „Was du mitbringst", „Dein Profil", „Anforderungen", „Requirements" …;
    - nice to have: „Ein Plus", „kein Muss", „Wünschenswert", „Nice to have" …;
    - other: „Deine Aufgaben", „Wir bieten", „Benefits" ….

    Unknown sub-headings („### Backend") do not end a section.
  - Markers in the sentence win over the section: „von Vorteil" means nice to have, „(Must-have)" means must-have.
  - Offered as an alternative to a covered skill — „oder", „z. B." lists — costs nothing.
  - Penalties: must-have 15, required 6, nice to have 4, together capped at 18 (the old cap). They are new
    Suchprofil fields; saved profiles get the defaults.
- **Seniority.** „Senior" in the title alone stays a weak signal (8 points). „Langjährig", „5+ Jahre", Lead,
  Principal or Staff in the title, and Senior/Lead inside an experience sentence are strong (10 points, configurable).
- **Required German** (`GermanRequirementDetector`, column `Vacancies.RequiredGerman`).
  - Five buckets with phrase lists: C1Plus, B2, B1OrLower, None, Unknown.
  - The strongest finding wins.
  - CEFR levels are matched in both orders („Deutsch B2", „C1 … Deutsch"). A level or quality right after another
    language („Englisch C1", „Englisch verhandlungssicher") does not count for German.
  - `None` is set only when German is explicitly not required, or when the posting is in English and mentions no
    German requirement.
  - The fit penalty uses the same detector.
  - It feeds the list filter (with facet counts) and the statistics page.
- **Himalayas = direct employer** unless agency signals are found, because Himalayas lists the hiring company itself.
- **Regression safety.** Real postings are kept as fixtures together with an invented profile of the same shape
  (`RankingRegressionTests`). Each asserts a rank range, not an exact number.

## Consequences

- With my real profile and saved Suchprofil:

  | Posting | Rank before | Rank after |
  |---|---|---|
  | MEDIFOX | 59 | 36 |
  | AraCom .NET | 87 | 87 |
  | Next.js junior | 16 | 7 |

- Optional gaps now cost 4 points instead of 0. Alternatives to covered skills still cost nothing.
- Vacancies stored before this step have `RequiredGerman = Unknown` until the next re-scoring, which runs at start-up.
- The .NET regex source generator threw `IndexOutOfRangeException` for two patterns: a negative lookbehind, and
  `sicher\b(?!\w)` after a lazy group. Both were rewritten without these constructs, and the detector tests cover
  them.

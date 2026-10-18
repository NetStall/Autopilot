# ADR 0004 — Pre-filter by title at import, scoring later (step 03b)

Status: accepted · 2026-10-18

## Context

Broader searches (region + 70 km, Germany-wide remote) bring more postings, and some of them are obviously not
jobs for an experienced developer: apprenticeships, internships, working students, theses, mini-jobs. Real
relevance scoring against the profile is step 04.

## Decision

At import, a vacancy whose **title** contains one of `Import:ExcludeTitleTerms` as a whole word or phrase
(case-insensitive) is **not stored**. It is counted as `Excluded` in the import result and per search.

- **Configuration, not code.** The list lives in `appsettings.json`; changing it needs no deploy of new code.
- **Whole word or phrase.** "Ausbildung" excludes "Ausbildung zum Fachinformatiker", but not "Ausbildungsleiter";
  "Duales Studium" also matches with several spaces. Matching on substrings would drop real jobs.
- **Title only.** The search result has no description yet (ADR 0002), and the title is what decides these cases.
- **Not stored instead of hidden.** These postings have no value later either; storing them would only grow the
  table and the "Neu" count. Because they are counted, a too aggressive term shows up in the import summary.
- **Existing rows are kept.** Adding a term later does not delete vacancies imported before.

## Why not do it in scoring (step 04)?

Scoring ranks; it should not have to deal with postings that are never relevant. A coarse, explainable
pre-filter keeps the data clean and scoring simple. Anything that needs judgement (stack match, seniority,
agencies) stays in step 04 and hides or ranks — it does not delete.

## Related findings

Most noise in the first broader import did not come from apprenticeships but from imprecise keywords (bare `C#`
matched driving licence class C). That is fixed in the search configuration, not by the title filter — see
[arbeitsagentur-api.md](../arbeitsagentur-api.md).

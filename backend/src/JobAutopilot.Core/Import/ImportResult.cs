namespace JobAutopilot.Core.Import;

/// <summary>
/// Outcome of an import run. <see cref="Fetched"/> counts every result the source returned (duplicates included);
/// <see cref="Added"/> + <see cref="Updated"/> + <see cref="Unchanged"/> count distinct vacancies.
/// A known vacancy is <see cref="Updated"/> only if at least one stored field actually changed.
/// </summary>
public sealed record ImportResult(
    int Fetched,
    int Added,
    int Updated,
    int Unchanged,
    IReadOnlyList<SearchImportResult> Searches);

/// <summary>Counts for one search. A vacancy already handled by an earlier search of the same run is not counted again.</summary>
public sealed record SearchImportResult(VacancySearch Search, int Fetched, int Added, int Updated, int Unchanged);

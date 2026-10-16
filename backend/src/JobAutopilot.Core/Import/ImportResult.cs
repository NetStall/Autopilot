namespace JobAutopilot.Core.Import;

/// <summary>
/// Outcome of an import run. <see cref="Fetched"/> counts every result the source returned (duplicates included);
/// <see cref="Added"/> + <see cref="Updated"/> count distinct vacancies.
/// </summary>
public sealed record ImportResult(int Fetched, int Added, int Updated, IReadOnlyList<SearchImportResult> Searches);

/// <summary>Counts for one search. A vacancy already handled by an earlier search of the same run is not counted again.</summary>
public sealed record SearchImportResult(VacancySearch Search, int Fetched, int Added, int Updated);

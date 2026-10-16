using JobAutopilot.Core.Import;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>Configuration section <c>Import</c>: the searches run by an import without a request body.</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public List<VacancySearch> Searches { get; init; } = [];
}

using JobAutopilot.Core.Import;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>Configuration section <c>Import</c>: the searches run by an import without a request body.</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public List<VacancySearch> Searches { get; init; } = [];

    /// <summary>Vacancies whose title contains one of these words or phrases are not stored (coarse pre-filter).</summary>
    public List<string> ExcludeTitleTerms { get; init; } = [];
}

/// <summary>Fails the start of the app when the configured searches are invalid (e.g. duplicate names).</summary>
internal sealed class ImportOptionsValidator : IValidateOptions<ImportOptions>
{
    public ValidateOptionsResult Validate(string? name, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = VacancySearchRules.Validate(options.Searches).ToList();

        if (options.ExcludeTitleTerms.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("ExcludeTitleTerms must not contain empty entries.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

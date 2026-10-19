using JobAutopilot.Core;
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

    public ImportScheduleOptions Schedule { get; init; } = new();

    /// <summary><c>Import:Details</c> — descriptions fetched after the search, per vacancy.</summary>
    public VacancyDetailsOptions Details { get; init; } = new();
}

/// <summary><c>Import:Schedule</c> — the daily automatic import (fetching only, never sending anything).</summary>
public sealed class ImportScheduleOptions
{
    public bool Enabled { get; init; }

    /// <summary>Local time of day, <c>HH:mm</c>.</summary>
    public string TimeOfDay { get; init; } = "07:00";

    /// <summary>IANA time zone id.</summary>
    public string TimeZone { get; init; } = "Europe/Berlin";

    public DailySchedule ToSchedule() =>
        new(TimeOnly.ParseExact(TimeOfDay, "HH:mm", System.Globalization.CultureInfo.InvariantCulture), TimeZones.Find(TimeZone));
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

        if (!TimeOnly.TryParseExact(options.Schedule.TimeOfDay, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            errors.Add($"Schedule:TimeOfDay '{options.Schedule.TimeOfDay}' must be HH:mm, e.g. 07:00.");
        }

        if (string.IsNullOrWhiteSpace(options.Schedule.TimeZone) || !TimeZones.TryFind(options.Schedule.TimeZone, out _))
        {
            errors.Add($"Schedule:TimeZone '{options.Schedule.TimeZone}' is not a known time zone.");
        }

        if (options.Details.MaxPerRun is < 0 or > 5000)
        {
            errors.Add("Details:MaxPerRun must be between 0 and 5000.");
        }

        if (options.Details.DelayBetweenRequestsMs is < 0 or > 10_000)
        {
            errors.Add("Details:DelayBetweenRequestsMs must be between 0 and 10000.");
        }

        if (options.Details.SaveEvery is < 1 or > 1000)
        {
            errors.Add("Details:SaveEvery must be between 1 and 1000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

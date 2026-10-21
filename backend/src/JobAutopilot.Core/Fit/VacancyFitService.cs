using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Fit;

/// <summary>
/// Runs the <see cref="FitAnalyzer"/> with the currently loaded profile and the Suchprofil's settings; without a usable
/// profile: no fit.
/// </summary>
public sealed class VacancyFitService(
    FitAnalyzer analyzer,
    ICandidateProfileSource profiles,
    TimeProvider timeProvider,
    ISearchProfileSource? searchProfiles = null)
{
    public FitResult Analyze(Vacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(vacancy);
        return Analyze(vacancy, Extraction.StoredExtraction.ForDescription(vacancy.ExtractionJson, vacancy.Description)?.Result, vacancy.RemoteLevel, vacancy.RemotePartial);
    }

    /// <summary>The fit without any AI input: no extraction, the remote level the rules alone found (07c).</summary>
    public FitResult AnalyzeRules(Vacancy vacancy, RemoteLevel remoteLevel, bool remotePartial)
    {
        ArgumentNullException.ThrowIfNull(vacancy);
        return Analyze(vacancy, null, remoteLevel, remotePartial);
    }

    private FitResult Analyze(Vacancy vacancy, Extraction.RequirementExtraction? extraction, RemoteLevel remoteLevel, bool remotePartial)
    {
        return profiles.Current.Usable is { } profile
            ? analyzer.Analyze(
                profile,
                new FitInput(vacancy.Title, vacancy.Description, vacancy.EmployerType, vacancy.Location, remoteLevel, vacancy.HomeOfficePercent,
                    extraction, remotePartial),
                DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
                searchProfiles is null ? FitSettings.Neutral : FitSettings.From(searchProfiles.Current.Profile))
            : FitResult.Empty;
    }
}

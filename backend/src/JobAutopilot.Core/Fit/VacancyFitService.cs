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
        return profiles.Current.Usable is { } profile
            ? analyzer.Analyze(
                profile,
                new FitInput(vacancy.Title, vacancy.Description, vacancy.EmployerType, vacancy.Location, vacancy.RemoteLevel, vacancy.HomeOfficePercent,
                    Extraction.StoredExtraction.ForDescription(vacancy.ExtractionJson, vacancy.Description)?.Result,
                    vacancy.RemotePartial),
                DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
                searchProfiles is null ? FitSettings.Neutral : FitSettings.From(searchProfiles.Current.Profile))
            : FitResult.Empty;
    }
}

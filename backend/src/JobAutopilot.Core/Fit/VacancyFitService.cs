using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Fit;

/// <summary>Runs the <see cref="FitAnalyzer"/> with the currently loaded profile; without a usable profile: no fit.</summary>
public sealed class VacancyFitService(FitAnalyzer analyzer, ICandidateProfileSource profiles, TimeProvider timeProvider)
{
    public FitResult Analyze(Vacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(vacancy);
        return profiles.Current.Usable is { } profile
            ? analyzer.Analyze(
                profile,
                new FitInput(vacancy.Title, vacancy.Description, vacancy.EmployerType, vacancy.Location, vacancy.RemoteLevel),
                DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            : FitResult.Empty;
    }
}

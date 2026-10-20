using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Fit;

/// <summary>What the fit check takes from the Suchprofil: penalties, my region, remote wish, employer types ranked lower.</summary>
public sealed record FitSettings(
    SearchPenalties Penalties,
    IReadOnlyList<string> Places,
    bool RemoteGermany,
    RemotePreference RemotePreference,
    IReadOnlyList<EmployerType> RankedLowerEmployerTypes)
{
    /// <summary>No region, no remote wish, default penalties.</summary>
    public static readonly FitSettings Neutral = new(new SearchPenalties(), [], true, RemotePreference.Any, []);

    public static FitSettings From(SearchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new FitSettings(
            profile.Penalties,
            [.. profile.Places.Select(p => p.City.Trim()).Concat(profile.AdditionalPlaces.Select(p => p.Trim())).Where(p => p.Length > 0)],
            profile.RemoteGermany,
            profile.RemotePreference,
            profile.RankedLowerEmployerTypes());
    }
}

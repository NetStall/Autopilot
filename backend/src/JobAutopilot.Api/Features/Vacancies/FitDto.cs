using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;

namespace JobAutopilot.Api.Features.Vacancies;

/// <summary>Response of <c>GET /api/vacancies/{id}/fit</c>.</summary>
/// <param name="ProfileStatus">Without a loaded profile there is no fit check (empty lists).</param>
/// <param name="HasDescription">Without a description only the title was checked.</param>
public sealed record FitDto(
    ProfileStatus ProfileStatus,
    bool HasDescription,
    IReadOnlyList<FitRequirement> Requirements,
    IReadOnlyList<FitWarning> Warnings,
    LanguageLevel? RequiredGerman,
    int? RequiredYears,
    int Penalty,
    Core.Fit.FitSource Source = Core.Fit.FitSource.Rules);

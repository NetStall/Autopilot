using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Infrastructure.Profile;
using JobAutopilot.Tests.Unit.Profile;

namespace JobAutopilot.Tests.Unit.Documents;

/// <summary>The example profile (invented data) and a standard German posting for the documents tests.</summary>
internal static class DocumentTestData
{
    public static readonly DateOnly Today = new(2026, 10, 20);

    public const string Title = "Full-Stack-Entwickler (m/w/d)";

    public const string Description =
        "Wir suchen Verstärkung für unser Team. Sie entwickeln mit C#, ASP.NET Core, Vue.js und Angular. " +
        "Kubernetes setzen wir im Betrieb ein und ist ein Muss. Wir freuen uns auf Ihre Bewerbung.";

    public static readonly FitOptions FitOptions = new()
    {
        CommonRequirements = [new FitRequirementRule { Name = "Kubernetes", Terms = ["K8s"] }, new FitRequirementRule { Name = "React" }],
        OptionalMarkers = ["von Vorteil", "wünschenswert", "nice to have"],
    };

    public static IEnumerable<string> KnownTechnologies => FitOptions.CommonRequirements.SelectMany(r => r.Terms.Append(r.Name));

    public static CandidateProfile Profile() =>
        FileCandidateProfileSource.Parse(ProfileTests.ExampleJson, Today).Profile
        ?? throw new InvalidOperationException("The example profile must be valid.");

    public static LetterInput Input(string title = Title, string? description = Description, SearchProfile? searchProfile = null)
    {
        var profile = Profile();
        var vacancy = new LetterVacancy(title, "Beispiel GmbH", "94469 Deggendorf", description, "Arbeitsagentur");
        var fit = new FitAnalyzer(FitOptions).Analyze(profile, title, description, Core.Vacancies.EmployerType.Direct, Today);
        return LetterFactsBuilder.Build(profile, vacancy, fit, searchProfile, Today);
    }
}

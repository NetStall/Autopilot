using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Profile;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>GET /api/profile and /api/vacancies/{id}/fit with the committed example profile.</summary>
public class ProfileAndFitApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Profile:Path"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "profile.example.json"),
    };

    [Fact]
    public async Task Profile_endpoint_returns_the_loaded_profile()
    {
        var profile = await Client.GetFromJsonAsync<ProfileDto>("/api/profile", ApiJson, CancellationToken);

        profile!.Status.Should().Be(ProfileStatus.Loaded);
        profile.Errors.Should().BeEmpty();
        profile.ExperienceYears.Should().Be(4);
        profile.Profile!.Personal.Name.Should().Be("Alex Beispiel");
    }

    [Fact]
    public async Task Fit_endpoint_classifies_requirements_against_the_profile()
    {
        var vacancy = Make(title: "Fullstack Entwickler C# / Angular");
        vacancy.Description = "Backend mit ASP.NET Core, Frontend mit Angular. Docker ist von Vorteil. Sehr gute Deutschkenntnisse.";
        await using (var db = Postgres.CreateDbContext())
        {
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync(CancellationToken);
        }

        var fit = await Client.GetFromJsonAsync<FitDto>($"/api/vacancies/{vacancy.Id}/fit", ApiJson, CancellationToken);

        fit!.ProfileStatus.Should().Be(ProfileStatus.Loaded);
        fit.Requirements.Should().Contain(r => r.Name == "C#" && r.Coverage == FitCoverage.Covered);
        fit.Requirements.Should().Contain(r => r.Name == "Angular" && r.Coverage == FitCoverage.Gap && !r.Optional);
        fit.Requirements.Should().Contain(r => r.Name == "Docker" && r.Coverage == FitCoverage.Partial && r.Optional);
        fit.RequiredGerman.Should().Be(LanguageLevel.C1);
        fit.Penalty.Should().Be(6 + 8, "one required gap, C1 is one level above the example's B2");

        (await Client.GetAsync($"/api/vacancies/{Guid.NewGuid()}/fit", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>Without a profile file the API keeps working and says what is missing.</summary>
public class MissingProfileApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Profile:Path"] = Path.Combine(AppContext.BaseDirectory, "does-not-exist", "profile.json"),
    };

    [Fact]
    public async Task Missing_profile_gives_a_clear_status_and_an_empty_fit()
    {
        var profile = await Client.GetFromJsonAsync<ProfileDto>("/api/profile", ApiJson, CancellationToken);
        profile!.Status.Should().Be(ProfileStatus.Missing);
        profile.Errors.Single().Should().Contain("profile.example.json");

        var vacancy = Make(title: "Softwareentwickler C#");
        await using (var db = Postgres.CreateDbContext())
        {
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync(CancellationToken);
        }

        var fit = await Client.GetFromJsonAsync<FitDto>($"/api/vacancies/{vacancy.Id}/fit", ApiJson, CancellationToken);
        fit!.ProfileStatus.Should().Be(ProfileStatus.Missing);
        fit.Requirements.Should().BeEmpty();
    }
}

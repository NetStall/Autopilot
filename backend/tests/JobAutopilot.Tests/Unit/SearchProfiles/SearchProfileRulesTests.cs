using FluentAssertions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Unit.Scoring;

namespace JobAutopilot.Tests.Unit.SearchProfiles;

public class SearchProfileRulesTests
{
    private static SearchProfile Valid() => new()
    {
        SearchTerms =
        {
            new SearchTerm { Text = "Softwareentwickler", Scope = SearchTermScope.Both },
            new SearchTerm { Text = "C# .NET", Scope = SearchTermScope.Region },
            new SearchTerm { Text = "C#", Scope = SearchTermScope.Remote },
        },
        Places = { new SearchPlace { City = "Deggendorf", RadiusKm = 70 } },
        RemoteGermany = true,
        MustHave = { new StackKeyword { Name = "C#", Weight = 25, Core = true } },
        TitleExclusions = { "Ausbildung" },
    };

    [Fact]
    public void A_complete_profile_is_valid() => SearchProfileRules.Validate(Valid()).Should().BeEmpty();

    public static TheoryData<Action<SearchProfile>, string> Broken => new()
    {
        { p => p.SearchTerms.Clear(), "Suchbegriffe: mindestens ein Suchbegriff ist nötig." },
        { p => p.SearchTerms.Add(new SearchTerm { Text = "softwareentwickler" }), "Suchbegriffe: „Softwareentwickler\" ist doppelt." },
        { p => p.SearchTerms.Add(new SearchTerm { Text = " " }), "Suchbegriffe: leere Einträge entfernen." },
        { p => p.Places.Add(new SearchPlace { City = "deggendorf", RadiusKm = 10 }), "Orte: „Deggendorf\" ist doppelt." },
        { p => p.Places[0].RadiusKm = 500, "Orte: Umkreis zwischen 0 und 200 km." },
        { p => { p.Places.Clear(); p.RemoteGermany = false; }, "Orte: mindestens ein Ort oder „Remote deutschlandweit“ ist nötig." },
        { p => p.Sources = new SearchSources { Arbeitsagentur = false, Himalayas = false }, "Quellen: mindestens eine Quelle einschalten." },
        { p => p.MinScore = 62, "Mindestbewertung: 0 bis 100 in 5er-Schritten." },
        { p => p.TopN = 0, "Top N: 1 bis 200." },
        { p => p.Penalties.OutsideRegion = 80, "Fit-Abzüge: „Außerhalb der Region\" zwischen 0 und 50." },
        { p => p.MustHave.Add(new StackKeyword { Name = "Vue", Weight = 0 }), "Must-have: Gewicht zwischen 1 und 50." },
        { p => p.Exclude.Add(new StackKeyword { Name = "Java", Weight = 30 }) , "" },
        { p => { p.SalaryMin = 70000; p.SalaryMax = 50000; }, "Gehaltsziel: von ≤ bis, keine negativen Werte." },
        { p => p.EarliestStart = "1.11.2026", "Frühester Eintritt: Datum als JJJJ-MM-TT." },
        { p => p.PublishedWithinDays = 0, "Veröffentlicht innerhalb: 1 bis 100 Tage." },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void Broken_values_are_reported_in_german(Action<SearchProfile> breakIt, string expected)
    {
        var profile = Valid();
        breakIt(profile);

        var errors = SearchProfileRules.Validate(profile);

        if (expected.Length == 0)
        {
            errors.Should().BeEmpty();
        }
        else
        {
            errors.Should().Contain(expected);
        }
    }

    [Fact]
    public void Searches_are_generated_from_terms_places_and_remote_with_stable_names()
    {
        var searches = SearchProfileRules.GenerateSearches(Valid());

        searches.Select(s => s.Name).Should().Equal(
            "region-softwareentwickler", "region-csharp-dotnet",
            "remote-softwareentwickler", "himalayas-softwareentwickler", "remote-csharp", "himalayas-csharp");
        searches[0].Should().BeEquivalentTo(new VacancySearch
        {
            Name = "region-softwareentwickler", Keyword = "Softwareentwickler", Location = "Deggendorf", RadiusKm = 70,
        });
        searches.Single(s => s.Name == "himalayas-csharp").Should().BeEquivalentTo(new { Source = VacancySource.Himalayas, RemoteOnly = true, Location = (string?)null });
    }

    [Fact]
    public void Several_places_get_the_place_in_the_name_and_disabled_sources_produce_nothing()
    {
        var profile = Valid();
        profile.Places.Add(new SearchPlace { City = "Regensburg", RadiusKm = 30 });
        profile.Sources.Himalayas = false;

        var names = SearchProfileRules.GenerateSearches(profile).Select(s => s.Name).ToList();

        names.Should().Contain("region-deggendorf-softwareentwickler").And.Contain("region-regensburg-csharp-dotnet");
        names.Should().NotContain(n => n.StartsWith("himalayas-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("C#", "csharp")]
    [InlineData(".NET Entwickler", "dotnet-entwickler")]
    [InlineData("Full Stack", "full-stack")]
    [InlineData("Straße / Süd", "strasse-sued")]
    [InlineData("C++", "cplusplus")]
    public void Slugs_are_url_and_name_safe(string text, string slug) => SearchProfileRules.Slug(text).Should().Be(slug);

    [Fact]
    public void The_change_summary_lists_what_changed()
    {
        var before = Valid();
        var after = Valid();
        after.SearchTerms.Add(new SearchTerm { Text = "Blazor" });
        after.MinScore = 60;
        after.RemoteGermany = false;

        SearchProfileRules.Summarize(before, after).Should()
            .Be("Suchbegriffe: +Blazor (Both); Remote deutschlandweit an → aus; Mindestbewertung 0 → 60");
        SearchProfileRules.Summarize(before, Valid()).Should().Be("Keine Änderung");
        SearchProfileRules.Summarize(null, after).Should().Be("Angelegt");
    }

    [Fact]
    public void Hidden_and_ranked_lower_employer_types_follow_the_policies()
    {
        var profile = Valid();
        profile.Agency = EmployerTypePolicy.RankLower;
        profile.Temporary = EmployerTypePolicy.Hide;

        profile.HiddenEmployerTypes().Should().Equal(EmployerType.Temporary);
        profile.RankedLowerEmployerTypes().Should().Equal(EmployerType.Agency);
        FitSettings.From(profile).RankedLowerEmployerTypes.Should().Equal(EmployerType.Agency);
    }

    [Fact]
    public void The_seed_takes_the_old_configuration_and_moves_preferences_out_of_profile_json()
    {
        var searches = new[]
        {
            new VacancySearch { Name = "region-softwareentwickler", Keyword = "Softwareentwickler", Location = "Deggendorf", RadiusKm = 70 },
            new VacancySearch { Name = "region-csharp", Keyword = "C# .NET", Location = "Deggendorf", RadiusKm = 70 },
            new VacancySearch { Name = "remote-softwareentwickler", Keyword = "Softwareentwickler", RemoteOnly = true },
            new VacancySearch { Name = "himalayas-vue", Keyword = "Vue", RemoteOnly = true, Source = VacancySource.Himalayas },
        };
        var legacy = new Preferences
        {
            SalaryMin = 52000, SalaryMax = 60000, Regions = { "Deggendorf", "Straubing", "Remote" }, Remote = "Hybrid",
            EarliestStart = "2026-11-01", AvoidEmployerTypes = { EmployerType.Temporary },
        };

        var profile = SearchProfileSeed.Create(searches, ["Ausbildung", "Praktikum"], ShippedConfig.Relevance(),
            new FitPenalties(), new SearchProfileDefaults(), legacy);

        profile.SearchTerms.Select(t => (t.Text, t.Scope)).Should().Equal(
            ("Softwareentwickler", SearchTermScope.Both), ("C# .NET", SearchTermScope.Region), ("Vue", SearchTermScope.Remote));
        profile.Places.Should().ContainSingle().Which.Should().BeEquivalentTo(new { City = "Deggendorf", RadiusKm = 70 });
        profile.Sources.Should().BeEquivalentTo(new { Arbeitsagentur = true, Himalayas = true });
        profile.MustHave.Select(k => k.Name).Should().Contain(["C#", ".NET", "Vue"]);
        profile.NiceToHave.Select(k => k.Name).Should().Contain("Softwareentwicklung");
        profile.Exclude.Select(k => k.Name).Should().Contain("Java");
        profile.AdditionalPlaces.Should().Equal("Straubing");
        profile.Should().BeEquivalentTo(new
        {
            SalaryMin = 52000, SalaryMax = 60000, EarliestStart = "2026-11-01",
            RemotePreference = RemotePreference.AtLeastHybrid, Temporary = EmployerTypePolicy.RankLower,
        });
        SearchProfileRules.Validate(profile).Should().BeEmpty();
        SearchProfileSeed.HasLegacyPreferences(new CandidateProfile { Preferences = legacy }).Should().BeTrue();
        SearchProfileSeed.HasLegacyPreferences(new CandidateProfile()).Should().BeFalse();
    }
}

using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Unit.Import;

/// <summary>Exclusion, remote flag and matched searches in <see cref="VacancyImportService"/>.</summary>
public class BroaderImportTests
{
    private static readonly VacancySearch Region = new()
    {
        Name = "region-csharp", Keyword = "C#", Location = "Deggendorf", RadiusKm = 70,
    };

    private static readonly VacancySearch Remote = new() { Name = "remote-csharp", Keyword = "C#", RemoteOnly = true };

    private static readonly VacancySearch RegionDotNet = new()
    {
        Name = "region-dotnet", Keyword = ".NET Entwickler", Location = "Deggendorf", RadiusKm = 70,
    };

    private static readonly TitleExclusionFilter Exclusion = new(["Ausbildung", "Werkstudent", "Praktikum"]);

    private readonly FakeVacancySource _source = new();
    private readonly InMemoryVacancyStore _store = new();
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void MarkFoundBy_adds_each_name_once_and_never_lowers_the_remote_level()
    {
        var vacancy = Vacancy.CreateManual("Dev", "Firma", _clock.GetUtcNow());

        vacancy.MarkFoundBy("remote-csharp", remoteOnly: true).Should().BeTrue();
        vacancy.MarkFoundBy("REMOTE-CSHARP", remoteOnly: true).Should().BeFalse();
        vacancy.MarkFoundBy("region-csharp", remoteOnly: false).Should().BeTrue();

        vacancy.MatchedSearches.Should().Equal("remote-csharp", "region-csharp");
        vacancy.RemoteLevel.Should().Be(RemoteLevel.Hybrid);
    }

    [Fact]
    public async Task Excluded_titles_are_not_stored_and_counted()
    {
        _source.Returns(Region.Name,
            Candidate("A", "Softwareentwickler C# (m/w/d)"),
            Candidate("B", "Ausbildung Fachinformatiker (m/w/d)"),
            Candidate("C", "Werkstudent Softwareentwicklung"),
            Candidate("D", "Ausbildungsleiter IT (m/w/d)"));

        var result = await ImportAsync(Region);

        result.Should().BeEquivalentTo(new { Fetched = 4, Added = 2, Updated = 0, Unchanged = 0, Excluded = 2 });
        result.Searches.Single().Excluded.Should().Be(2);
        _store.Saved.Select(v => v.ExternalId).Should().BeEquivalentTo("A", "D");
    }

    [Fact]
    public async Task An_excluded_vacancy_found_by_two_searches_counts_once()
    {
        _source
            .Returns(Region.Name, Candidate("B", "Praktikum C#"))
            .Returns(Remote.Name, Candidate("B", "Praktikum C#"));

        var result = await ImportAsync(Region, Remote);

        result.Excluded.Should().Be(1);
        result.Fetched.Should().Be(2);
        _store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Remote_search_marks_vacancy_as_remote_and_region_search_keeps_it()
    {
        _source.Returns(Remote.Name, Candidate("A")).Returns(Region.Name, Candidate("A"), Candidate("B"));

        await ImportAsync(Remote);
        await ImportAsync(Region);

        var a = _store.Saved.Single(v => v.ExternalId == "A");
        var b = _store.Saved.Single(v => v.ExternalId == "B");
        a.RemoteLevel.Should().Be(RemoteLevel.Hybrid, "a later non-remote search must not lower the level");
        a.MatchedSearches.Should().Equal("remote-csharp", "region-csharp");
        b.RemoteLevel.Should().Be(RemoteLevel.Unknown);
        b.MatchedSearches.Should().Equal("region-csharp");
    }

    [Fact]
    public async Task Matched_searches_are_merged_within_one_run_without_duplicates()
    {
        _source
            .Returns(Region.Name, Candidate("A"), Candidate("A"))
            .Returns(Remote.Name, Candidate("A"))
            .Returns(RegionDotNet.Name, Candidate("A"));

        var result = await ImportAsync(Region, Remote, RegionDotNet, Region);

        var vacancy = _store.Saved.Single();
        vacancy.MatchedSearches.Should().Equal("region-csharp", "remote-csharp", "region-dotnet");
        vacancy.RemoteLevel.Should().Be(RemoteLevel.Hybrid);
        result.Added.Should().Be(1);
        result.Searches.Select(s => s.Added).Should().Equal(1, 0, 0, 0);
        result.Fetched.Should().Be(6);
    }

    [Fact]
    public async Task Repeated_imports_do_not_duplicate_names_and_count_unchanged()
    {
        _source.Returns(Region.Name, Candidate("A")).Returns(Remote.Name, Candidate("A"));
        await ImportAsync(Region, Remote);

        var second = await ImportAsync(Region, Remote);

        _store.Saved.Single().MatchedSearches.Should().Equal("region-csharp", "remote-csharp");
        second.Should().BeEquivalentTo(new { Added = 0, Updated = 0, Unchanged = 1, Excluded = 0 });
    }

    [Fact]
    public async Task Being_found_by_a_new_search_counts_as_updated()
    {
        _source.Returns(Region.Name, Candidate("A")).Returns(Remote.Name, Candidate("A"));
        await ImportAsync(Region);

        var second = await ImportAsync(Region, Remote);

        second.Should().BeEquivalentTo(new { Added = 0, Updated = 1, Unchanged = 0 });
        second.Searches[0].Updated.Should().Be(1, "the vacancy is counted for the first search that found it");
        _store.Saved.Single().RemoteLevel.Should().Be(RemoteLevel.Hybrid);
    }

    [Fact]
    public async Task Counts_add_up_to_the_distinct_candidates()
    {
        _store.Saved.Add(Existing("KNOWN"));
        _source
            .Returns(Region.Name, Candidate("NEW"), Candidate("KNOWN"), Candidate("X", "Ausbildung C#"))
            .Returns(Remote.Name, Candidate("NEW"), Candidate("REMOTE"));

        var result = await ImportAsync(Region, Remote);

        result.Fetched.Should().Be(5);
        (result.Added + result.Updated + result.Unchanged + result.Excluded).Should().Be(4);
        result.Should().BeEquivalentTo(new { Added = 2, Updated = 1, Unchanged = 0, Excluded = 1 });
        result.Searches[0].Should().BeEquivalentTo(new { Fetched = 3, Added = 1, Updated = 1, Excluded = 1 });
        result.Searches[1].Should().BeEquivalentTo(new { Fetched = 2, Added = 1, Updated = 0, Excluded = 0 });
    }

    private Vacancy Existing(string externalId)
    {
        var vacancy = Vacancy.CreateManual("Alter Titel", "Firma", _clock.GetUtcNow().AddDays(-3));
        vacancy.Source = VacancySource.Arbeitsagentur;
        vacancy.ExternalId = externalId;
        return vacancy;
    }

    private Task<ImportResult> ImportAsync(params VacancySearch[] searches) =>
        new VacancyImportService(_store, _clock).ImportAsync(_source, searches, Exclusion, CancellationToken);
}

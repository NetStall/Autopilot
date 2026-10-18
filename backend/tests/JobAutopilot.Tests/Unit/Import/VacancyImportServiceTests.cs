using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Unit.Import;

public class VacancyImportServiceTests
{
    private static readonly VacancySearch DotNet = new()
    {
        Name = "region-dotnet", Keyword = ".NET Entwickler", Location = "Deggendorf", RadiusKm = 50, PublishedWithinDays = 14,
    };
    private static readonly VacancySearch CSharp = new()
    {
        Name = "region-csharp", Keyword = "C# Entwickler", Location = "Regensburg", RadiusKm = 30, PublishedWithinDays = 14,
    };

    private readonly FakeVacancySource _source = new();
    private readonly InMemoryVacancyStore _store = new();
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.Zero));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_candidates_are_added_as_unseen_with_unknown_employer_type()
    {
        _source.Returns(DotNet.Keyword, Candidate("A"), Candidate("B"));

        var result = await ImportAsync(DotNet);

        result.Should().BeEquivalentTo(new { Fetched = 2, Added = 2, Updated = 0, Unchanged = 0 });
        _store.Saved.Should().HaveCount(2).And.AllSatisfy(v =>
        {
            v.Id.Should().NotBeEmpty();
            v.Source.Should().Be(VacancySource.Arbeitsagentur);
            v.EmployerType.Should().Be(EmployerType.Unknown);
            v.IsSeen.Should().BeFalse();
            v.FetchedAt.Should().Be(_clock.GetUtcNow());
        });
        _store.Saved[0].Should().BeEquivalentTo(Candidate("A"), options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task Duplicates_within_one_run_are_stored_once()
    {
        _source
            .Returns(DotNet.Keyword, Candidate("A"), Candidate("B"), Candidate("A"))
            .Returns(CSharp.Keyword, Candidate("B"), Candidate("C"));

        var result = await ImportAsync(DotNet, CSharp);

        result.Fetched.Should().Be(5);
        result.Added.Should().Be(3);
        result.Updated.Should().Be(0);
        result.Searches.Select(s => (s.Fetched, s.Added)).Should().Equal((3, 2), (2, 1));
        _store.Saved.Select(v => v.ExternalId).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task Existing_vacancy_is_updated_and_keeps_user_state()
    {
        var fetchedEarlier = _clock.GetUtcNow().AddDays(-3);
        var existing = new Vacancy
        {
            Id = Guid.NewGuid(),
            Source = VacancySource.Arbeitsagentur,
            ExternalId = "A",
            Title = "Alter Titel",
            Company = "Alte Firma",
            Location = null,
            Url = null,
            Description = "Notiz",
            EmployerType = EmployerType.Agency,
            PublishedAt = null,
            FetchedAt = fetchedEarlier,
            IsSeen = true,
        };
        _store.Saved.Add(existing);
        _source.Returns(DotNet.Keyword, Candidate("A", title: "Neuer Titel"), Candidate("B"));

        var result = await ImportAsync(DotNet);

        result.Should().BeEquivalentTo(new { Fetched = 2, Added = 1, Updated = 1, Unchanged = 0 });
        existing.Title.Should().Be("Neuer Titel");
        existing.Company.Should().Be("Beispiel GmbH");
        existing.Location.Should().Be("94469 Deggendorf");
        existing.Url.Should().Be("https://example.de/jobs/A");
        existing.PublishedAt.Should().Be(Candidate("A").PublishedAt);
        existing.IsSeen.Should().BeTrue("the import must never reset what the user has seen");
        existing.EmployerType.Should().Be(EmployerType.Agency);
        existing.Description.Should().Be("Notiz");
        existing.FetchedAt.Should().Be(fetchedEarlier);
    }

    [Fact]
    public async Task Identical_data_counts_as_unchanged_not_updated()
    {
        _source.Returns(DotNet.Keyword, Candidate("A"), Candidate("B"));
        await ImportAsync(DotNet);

        var second = await ImportAsync(DotNet);

        second.Should().BeEquivalentTo(new { Fetched = 2, Added = 0, Updated = 0, Unchanged = 2 });
        second.Searches.Single().Unchanged.Should().Be(2);
    }

    [Fact]
    public async Task Same_instant_in_another_offset_is_unchanged()
    {
        // The database returns UTC, the source delivers midnight in Berlin time.
        var stored = Candidate("A");
        _source.Returns(DotNet.Keyword, stored);
        await ImportAsync(DotNet);
        _store.Saved[0].PublishedAt = stored.PublishedAt!.Value.ToUniversalTime();

        var second = await ImportAsync(DotNet);

        second.Unchanged.Should().Be(1);
        second.Updated.Should().Be(0);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("company")]
    [InlineData("location")]
    [InlineData("url")]
    [InlineData("publishedAt")]
    public async Task Any_changed_field_counts_as_updated(string field)
    {
        var original = Candidate("A");
        _source.Returns(DotNet.Keyword, original);
        await ImportAsync(DotNet);
        var changed = field switch
        {
            "title" => original with { Title = "Neu" },
            "company" => original with { Company = "Andere GmbH" },
            "location" => original with { Location = null },
            "url" => original with { Url = "https://example.de/other" },
            _ => original with { PublishedAt = original.PublishedAt!.Value.AddDays(1) },
        };
        _source.Returns(DotNet.Keyword, changed);

        var second = await ImportAsync(DotNet);

        second.Should().BeEquivalentTo(new { Added = 0, Updated = 1, Unchanged = 0 });
    }

    [Fact]
    public async Task Vacancies_of_another_source_are_not_matched()
    {
        var manual = Vacancy.CreateManual("Dev", "Firma", _clock.GetUtcNow());
        manual.ExternalId = "A";
        _store.Saved.Add(manual);
        _source.Returns(DotNet.Keyword, Candidate("A"));

        var result = await ImportAsync(DotNet);

        result.Added.Should().Be(1);
        result.Updated.Should().Be(0);
    }

    [Fact]
    public async Task Everything_is_saved_once_at_the_end()
    {
        _source.Returns(DotNet.Keyword, Candidate("A")).Returns(CSharp.Keyword, Candidate("B"));

        await ImportAsync(DotNet, CSharp);

        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Nothing_is_saved_when_the_source_fails()
    {
        _source.IsUnavailable = true;

        var act = () => ImportAsync(DotNet);

        await act.Should().ThrowAsync<VacancySourceUnavailableException>();
        _store.SaveCount.Should().Be(0);
        _store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_configured_search_is_run()
    {
        await ImportAsync(DotNet, CSharp);

        _source.Searches.Should().Equal(DotNet, CSharp);
    }

    private Task<ImportResult> ImportAsync(params VacancySearch[] searches) =>
        new VacancyImportService(_store, _clock).ImportAsync(_source, searches, TitleExclusionFilter.None, CancellationToken);
}

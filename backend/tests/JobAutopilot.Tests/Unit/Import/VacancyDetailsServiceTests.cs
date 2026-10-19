using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using JobAutopilot.Tests.Unit.Scoring;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Unit.Import;

public class VacancyDetailsServiceTests
{
    private static readonly VacancyDetailsOptions Options = new() { MaxPerRun = 300, DelayBetweenRequestsMs = 0, SaveEvery = 2 };

    private readonly InMemoryVacancyStore _store = new();
    private readonly FakeDetailsSource _details = new();
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 7, 0, 0, TimeSpan.Zero));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fetches_descriptions_and_scores_the_vacancies()
    {
        var vacancy = Stored("A", "Softwareentwickler (m/w/d)");
        _details.Returns("A", new VacancyDetails("Wir entwickeln mit C# und .NET. 100 % remote.", RemoteLevel.Hybrid));

        var result = await FetchAsync();

        result.Should().Be(new DetailsResult(Requested: 1, Fetched: 1, NotFound: 0, Stopped: false));
        vacancy.Description.Should().StartWith("Wir entwickeln");
        vacancy.DetailsFetchedAt.Should().Be(_clock.GetUtcNow());
        vacancy.RemoteLevel.Should().Be(RemoteLevel.FullRemote, "the description says 100 % remote");
        vacancy.RelevanceLevel.Should().Be(RelevanceLevel.Relevant);
        vacancy.RelevanceReasons.Should().Contain("C# ✓ (+25)").And.Contain("Voll remote: „100 % remote“");
    }

    [Fact]
    public async Task Respects_the_cap_and_continues_with_the_rest_next_time()
    {
        for (var i = 0; i < 5; i++)
        {
            Stored($"V{i}", minutesAfterBase: i);
        }

        var first = await FetchAsync(Options with { MaxPerRun = 3 });
        var second = await FetchAsync(Options with { MaxPerRun = 3 });

        first.Requested.Should().Be(3);
        second.Requested.Should().Be(2);
        _details.Requested.Should().Equal("V4", "V3", "V2", "V1", "V0");
        _store.Saved.Should().OnlyContain(v => v.DetailsFetchedAt != null);
    }

    [Fact]
    public async Task Vacancies_that_already_have_details_are_not_requested_again()
    {
        Stored("A").DetailsFetchedAt = _clock.GetUtcNow().AddDays(-1);
        Stored("B");

        await FetchAsync();

        _details.Requested.Should().Equal("B");
    }

    [Fact]
    public async Task Gone_postings_are_marked_so_they_are_not_requested_forever()
    {
        var vacancy = Stored("GONE");

        var result = await FetchAsync();

        result.NotFound.Should().Be(1);
        vacancy.DetailsFetchedAt.Should().NotBeNull();
        vacancy.Description.Should().BeNull();
    }

    [Fact]
    public async Task An_unavailable_source_stops_the_pass_and_keeps_what_was_fetched()
    {
        Stored("A", minutesAfterBase: 3);
        Stored("B", minutesAfterBase: 2);
        Stored("C", minutesAfterBase: 1);
        _details.Returns("A", new VacancyDetails("Text A"));
        _details.FailFromRequest = 2;

        var result = await FetchAsync();

        result.Should().Be(new DetailsResult(Requested: 3, Fetched: 1, NotFound: 0, Stopped: true));
        _store.Saved.Single(v => v.ExternalId == "A").DetailsFetchedAt.Should().NotBeNull();
        _store.Saved.Where(v => v.ExternalId != "A").Should().OnlyContain(v => v.DetailsFetchedAt == null, "retried next time");
        _store.SaveCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Progress_is_saved_regularly_and_when_cancelled()
    {
        for (var i = 0; i < 5; i++)
        {
            Stored($"V{i}", minutesAfterBase: i);
        }

        using var cts = new CancellationTokenSource();
        var act = () => new VacancyDetailsService(_store, ShippedConfig.Enricher(), _clock).FetchAsync(
            _details, Options, cts.Token, (done, _, _) =>
            {
                if (done == 3)
                {
                    cts.Cancel();
                }

                return Task.CompletedTask;
            });

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Saved.Count(v => v.DetailsFetchedAt != null).Should().Be(3, "every finished vacancy is kept");
    }

    [Fact]
    public async Task Long_descriptions_are_truncated()
    {
        var vacancy = Stored("A");
        _details.Returns("A", new VacancyDetails(new string('x', VacancyLimits.DescriptionMaxLength + 100)));

        await FetchAsync();

        vacancy.Description.Should().HaveLength(VacancyLimits.DescriptionMaxLength);
    }

    [Fact]
    public async Task Import_refetches_details_when_the_posting_changed_at_the_source()
    {
        var source = new FakeVacancySource();
        var search = new VacancySearch { Name = "s", Keyword = "C#" };
        var importer = new VacancyImportService(_store, _clock, ShippedConfig.Enricher());
        var changedAt = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

        source.Returns("s", Candidate("A") with { ChangedAt = changedAt });
        await importer.ImportAsync(source, [search], TitleExclusionFilter.None, CancellationToken);
        await FetchAsync();
        var fetchedAt = _store.Saved.Single().DetailsFetchedAt;

        await importer.ImportAsync(source, [search], TitleExclusionFilter.None, CancellationToken);
        _store.Saved.Single().DetailsFetchedAt.Should().Be(fetchedAt, "unchanged at the source → cached");

        source.Returns("s", Candidate("A") with { ChangedAt = _clock.GetUtcNow().AddHours(1) });
        await importer.ImportAsync(source, [search], TitleExclusionFilter.None, CancellationToken);
        _store.Saved.Single().DetailsFetchedAt.Should().BeNull("changed after the details were fetched");
    }

    [Fact]
    public async Task Import_scores_new_vacancies_and_takes_the_remote_hint_of_the_source()
    {
        var source = new FakeVacancySource().Returns("s",
            Candidate("A", "Softwareentwickler C# (m/w/d)") with { RemoteHint = RemoteLevel.FullRemote },
            Candidate("B", "Java Developer (m/w/d)"));

        await new VacancyImportService(_store, _clock, ShippedConfig.Enricher())
            .ImportAsync(source, [new VacancySearch { Name = "s", Keyword = "x" }], TitleExclusionFilter.None, CancellationToken);

        var a = _store.Saved.Single(v => v.ExternalId == "A");
        a.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
        a.RelevanceLevel.Should().Be(RelevanceLevel.Relevant);
        _store.Saved.Single(v => v.ExternalId == "B").RelevanceLevel.Should().Be(RelevanceLevel.Irrelevant);
    }

    private Task<DetailsResult> FetchAsync(VacancyDetailsOptions? options = null) =>
        new VacancyDetailsService(_store, ShippedConfig.Enricher(), _clock).FetchAsync(_details, options ?? Options, CancellationToken);

    private Vacancy Stored(string externalId, string title = "Softwareentwickler (m/w/d)", int minutesAfterBase = 0)
    {
        var vacancy = Vacancy.CreateManual(title, "Firma", _clock.GetUtcNow().AddMinutes(minutesAfterBase));
        vacancy.Source = VacancySource.Arbeitsagentur;
        vacancy.ExternalId = externalId;
        _store.Saved.Add(vacancy);
        return vacancy;
    }
}

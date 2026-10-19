using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using JobAutopilot.Tests.Unit.Scoring;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Unit.Import;

/// <summary>Searches against several sources in one run; one source failing does not stop the others.</summary>
public class MultiSourceImportTests
{
    private static readonly VacancySearch Aa = new() { Name = "aa", Keyword = "C#" };
    private static readonly VacancySearch Hima = new() { Name = "hima", Keyword = "C#", Source = VacancySource.Himalayas, RemoteOnly = true };

    private readonly FakeVacancySource _aa = new();
    private readonly FakeVacancySource _himalayas = new(VacancySource.Himalayas);
    private readonly InMemoryVacancyStore _store = new();
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 7, 0, 0, TimeSpan.Zero));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_search_goes_to_its_source_and_the_same_id_in_two_sources_is_two_vacancies()
    {
        _aa.Returns("aa", Candidate("X-1", "C# Entwickler (m/w/d)"));
        _himalayas.Returns("hima", Candidate("X-1", "C# Entwickler (m/w/d)") with { RemoteHint = RemoteLevel.FullRemote });

        var result = await ImportAsync(Aa, Hima);

        result.Added.Should().Be(2);
        _aa.Searches.Should().ContainSingle().Which.Name.Should().Be("aa");
        _himalayas.Searches.Should().ContainSingle().Which.Name.Should().Be("hima");
        _store.Saved.Select(v => v.Source).Should().BeEquivalentTo([VacancySource.Arbeitsagentur, VacancySource.Himalayas]);
        _store.Saved.Select(v => v.DuplicateKey).Distinct().Should().ContainSingle("same company and title");
        _store.Saved.Single(v => v.Source == VacancySource.Himalayas).RemoteLevel.Should().Be(RemoteLevel.FullRemote);
    }

    [Fact]
    public async Task A_failing_source_is_recorded_and_the_other_searches_are_saved()
    {
        _aa.Returns("aa", Candidate("A-1"));
        _himalayas.IsUnavailable = true;

        var result = await ImportAsync(Aa, Hima);

        result.Added.Should().Be(1);
        result.Searches[1].Error.Should().Contain("Himalayas nicht erreichbar");
        result.Searches[0].Error.Should().BeNull();
        _store.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task The_run_fails_when_every_search_failed()
    {
        _aa.IsUnavailable = true;
        _himalayas.IsUnavailable = true;

        var act = () => ImportAsync(Aa, Hima);

        await act.Should().ThrowAsync<VacancySourceUnavailableException>();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task A_search_for_a_source_without_client_is_skipped_with_an_error()
    {
        _aa.Returns("aa", Candidate("A-1"));

        var result = await new VacancyImportService(_store, _clock, ShippedConfig.Enricher())
            .ImportAsync([_aa], [Aa, Hima], TitleExclusionFilter.None, CancellationToken);

        result.Searches[1].Error.Should().Be("Die Quelle Himalayas ist nicht eingerichtet.");
        _store.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task Candidates_the_source_excludes_are_counted_and_not_stored()
    {
        _himalayas.Returns("hima",
            Candidate("US") with { ExclusionReason = "Nur für Bewerber aus: United States" },
            Candidate("DE"));

        var result = await ImportAsync(Hima);

        result.Excluded.Should().Be(1);
        _store.Saved.Should().ContainSingle().Which.ExternalId.Should().Be("DE");
    }

    [Fact]
    public async Task Expiry_dates_are_stored_and_expired_vacancies_deleted()
    {
        _himalayas.Returns("hima",
            Candidate("OLD") with { ExpiresAt = _clock.GetUtcNow().AddDays(-1) },
            Candidate("NEW") with { ExpiresAt = _clock.GetUtcNow().AddDays(30) });
        await ImportAsync(Hima);

        var deleted = await _store.DeleteExpiredAsync(_clock.GetUtcNow(), CancellationToken);

        deleted.Should().Be(1);
        _store.Saved.Should().ContainSingle().Which.ExternalId.Should().Be("NEW");
    }

    private Task<ImportResult> ImportAsync(params VacancySearch[] searches) =>
        new VacancyImportService(_store, _clock, ShippedConfig.Enricher())
            .ImportAsync([_aa, _himalayas], searches, TitleExclusionFilter.None, CancellationToken);
}

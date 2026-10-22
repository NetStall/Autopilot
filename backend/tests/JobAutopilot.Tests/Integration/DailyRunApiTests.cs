using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Features.DailyRun;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Llm;
using JobAutopilot.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>„Tageslauf" (08b) with fake sources and a fake model probe: all stages, Ollama down, idempotency.</summary>
public class DailyRunApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly FakeVacancySource _source = new FakeVacancySource()
        .Returns("region-softwareentwickler", FakeVacancySource.Candidate("1", "Softwareentwickler C# (m/w/d)"), FakeVacancySource.Candidate("2", "Head of .NET Development (m/w/d)"));

    private readonly FakeProbe _probe = new();

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Llm:Tasks:Extraction"] = "ollama:qwen3:8b",
        ["Llm:Tasks:FitScore"] = "",
        ["Llm:Extraction:AutoAfterImport"] = "false",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        DailyRunWorker.Poll = TimeSpan.FromMilliseconds(20);
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
        services.RemoveAll<ILocalModelProbe>();
        services.AddSingleton<ILocalModelProbe>(_probe);
        services.RemoveAll<ILlmClient>();
        services.AddSingleton<ILlmClient>(new FakeLlmClient { Answer = _ => ExtractionApiTests.ValidJson });
    }

    [Fact]
    public async Task Without_the_local_model_the_ki_stage_is_skipped_and_the_rest_runs()
    {
        var summary = await RunAsync();

        summary.Stages.Select(s => (s.Stage, s.Outcome)).Should().Equal(
            (DailyRunStage.Import, StageOutcome.Done), (DailyRunStage.Filters, StageOutcome.Done), (DailyRunStage.Scoring, StageOutcome.Done),
            (DailyRunStage.Extraction, StageOutcome.Skipped), (DailyRunStage.Channels, StageOutcome.Done), (DailyRunStage.Summary, StageOutcome.Done));
        summary.Stages.Single(s => s.Stage == DailyRunStage.Extraction).Message.Should().Contain("nicht erreichbar");
        summary.NewVacancies.Should().Be(2);
        summary.NewFiltered.Should().ContainKey(Core.Filters.HardFilterReason.RoleType, "the Head-of job is filtered");
        summary.NewInTop20.Should().ContainSingle().Which.Title.Should().Be("Softwareentwickler C# (m/w/d)");
        summary.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_second_run_is_safe_and_finds_nothing_new_with_the_model_up()
    {
        await RunAsync();
        _probe.Reachable = true;

        var again = await RunAsync();

        again.NewVacancies.Should().Be(0);
        again.NewInTop20.Should().BeEmpty();
        again.Stages.Single(s => s.Stage == DailyRunStage.Extraction).Outcome.Should().Be(StageOutcome.Done);
        (await Client.PostAsync("/api/daily-run?top=0", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Coverage_counts_vacancies_per_search_and_names_the_ones_that_bring_nothing()
    {
        await RunAsync();

        var coverage = await Client.GetFromJsonAsync<CoverageDto>("/api/statistics/coverage?days=7", Json, CancellationToken);

        coverage!.Searches.Should().Contain(s => s.Name == "region-softwareentwickler" && s.Vacancies == 2 && s.InTop50 == 1);
        coverage.BringNothing.Should().NotContain("region-softwareentwickler").And.NotBeEmpty();
    }

    private async Task<DailyRunSummaryDto> RunAsync()
    {
        // A request clears the stages; the run is over when it is idle again with all six of them.
        (await Client.PostAsync("/api/daily-run?top=100", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = (await Client.GetFromJsonAsync<DailyRunStatusDto>("/api/daily-run", Json, CancellationToken))!;
            if (status is { State: DailyRunState.Idle, Stages.Count: 6, Last: { } last })
            {
                return last;
            }

            DateTime.UtcNow.Should().BeBefore(deadline, "the daily run finishes; stages so far: {0}", string.Join(", ", status.Stages.Select(s => $"{s.Stage} {s.Outcome} {s.Message}")));
            await Task.Delay(50, CancellationToken);
        }
    }

    private sealed class FakeProbe : ILocalModelProbe
    {
        public bool Reachable { get; set; }

        public Task<(bool Reachable, string? Error)> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult((Reachable, Reachable ? null : (string?)"Verbindung abgelehnt"));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Extraction;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>Requirement extraction (step 07a) with a scripted model — never a real Ollama.</summary>
public class ExtractionApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    internal const string ValidJson = """
        {"mustHave":["C#","Broadcast oder MAM"],"niceToHave":["Kubernetes"],"germanLevel":"C1","englishLevel":"B2",
         "minYears":3,"seniority":"mid","remoteLevel":"hybrid","homeOfficePercent":40,"employmentType":"fullTime",
         "domainRequirements":["Broadcast/MAM"],"summary":"Ein Backend-Job. Gefragt sind C# und Broadcast-Erfahrung."}
        """;

    private readonly FakeLlmClient _model = new();

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Profile:Path"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "profiles", "regression-profile.json"),
        ["Llm:Tasks:Extraction"] = "ollama:qwen3:8b",
        ["Llm:Extraction:AutoAfterImport"] = "false",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ILlmClient>();
        services.AddSingleton<ILlmClient>(_model);
    }

    private async Task<Vacancy> SeedAsync(string description, int rank = 50, string title = "Softwareentwickler (m/w/d)")
    {
        var vacancy = VacancySeed.Make(title: title);
        vacancy.Description = description;
        vacancy.RankScore = rank;
        vacancy.DetailsFetchedAt = vacancy.FetchedAt;
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(CancellationToken);
        return vacancy;
    }

    private async Task<ExtractionOutcome> ExtractAsync(Guid id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VacancyExtractionService>().ExtractAsync(id, CancellationToken);
    }

    private async Task<Vacancy> ReloadAsync(Guid id)
    {
        await using var db = Postgres.CreateDbContext();
        return await db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == id, CancellationToken);
    }

    [Fact]
    public async Task A_valid_answer_is_stored_cached_and_used_by_the_fit_check()
    {
        _model.Answer = _ => ValidJson;
        var vacancy = await SeedAsync("Wir suchen C#-Entwickler mit Broadcast-Erfahrung.");

        (await ExtractAsync(vacancy.Id)).Kind.Should().Be(ExtractionOutcomeKind.Extracted);

        var stored = await ReloadAsync(vacancy.Id);
        StoredExtraction.ForDescription(stored.ExtractionJson, stored.Description)!.Result.DomainRequirements.Should().Equal("Broadcast/MAM");
        stored.RelevanceReasons.Should().Contain("Anforderungen: KI (qwen3:8b, extraction.v2)");
        stored.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Muss-Anforderung, nicht im Profil:", StringComparison.Ordinal) && r.Contains("Broadcast", StringComparison.Ordinal) && r.Contains("MAM", StringComparison.Ordinal));

        var fit = await Client.GetFromJsonAsync<JsonElement>($"/api/vacancies/{vacancy.Id}/fit", CancellationToken);
        fit.GetProperty("source").GetString().Should().Be(nameof(FitSource.Ai));
        var detail = await Client.GetFromJsonAsync<JsonElement>($"/api/vacancies/{vacancy.Id}", CancellationToken);
        detail.GetProperty("extraction").GetProperty("model").GetString().Should().Be("qwen3:8b");
        detail.GetProperty("extraction").GetProperty("result").GetProperty("summary").GetString().Should().StartWith("Ein Backend-Job.");

        // Same text elsewhere (the posting also came from another source): from the cache, no second call.
        var duplicate = await SeedAsync("Wir suchen C#-Entwickler mit Broadcast-Erfahrung.");
        (await ExtractAsync(duplicate.Id)).Kind.Should().Be(ExtractionOutcomeKind.Cached);
        _model.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_invalid_answer_is_retried_once_with_the_errors()
    {
        var answers = new Queue<string>(["""{"mustHave":[],"germanLevel":"fließend"}""", ValidJson]);
        _model.Answer = _ => answers.Dequeue();
        var vacancy = await SeedAsync("Text");

        (await ExtractAsync(vacancy.Id)).Kind.Should().Be(ExtractionOutcomeKind.Extracted);

        _model.Requests.Should().HaveCount(2);
        _model.Requests[1].Operation.Should().Be("extract-retry");
        _model.Requests[1].User.Should().Contain("germanLevel must be one of");
    }

    [Fact]
    public async Task Invalid_twice_keeps_the_rules_and_is_not_asked_again()
    {
        _model.Answer = _ => "kein JSON";
        var vacancy = await SeedAsync("Wir suchen C#-Entwickler.");

        (await ExtractAsync(vacancy.Id)).Kind.Should().Be(ExtractionOutcomeKind.Failed);
        (await ExtractAsync(vacancy.Id)).Kind.Should().Be(ExtractionOutcomeKind.Failed);

        _model.Requests.Should().HaveCount(2, "one answer and one retry, then the failure is cached");
        var stored = await ReloadAsync(vacancy.Id);
        stored.ExtractionJson.Should().BeNull();
        stored.RelevanceReasons.Should().NotContain(r => r.StartsWith("Anforderungen: KI", StringComparison.Ordinal));
        await using var db = Postgres.CreateDbContext();
        (await db.VacancyExtractions.SingleAsync(CancellationToken)).Status.Should().Be(ExtractionStatus.Failed);
    }

    [Fact]
    public async Task A_changed_description_is_pending_again_and_the_old_extraction_is_ignored()
    {
        _model.Answer = _ => ValidJson;
        var vacancy = await SeedAsync("Alter Text");
        await ExtractAsync(vacancy.Id);

        await Client.PutAsJsonAsync($"/api/vacancies/{vacancy.Id}/description", new { description = "Neuer Text mit C#" }, CancellationToken);

        var stored = await ReloadAsync(vacancy.Id);
        StoredExtraction.ForDescription(stored.ExtractionJson, stored.Description).Should().BeNull("the hash belongs to the old text");
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VacancyExtractionService>().PendingAsync(10, CancellationToken)).Should().Equal(vacancy.Id);
    }

    [Fact]
    public async Task The_backfill_runs_most_relevant_first_and_single_requests_go_ahead()
    {
        _model.Answer = _ => ValidJson;
        var low = await SeedAsync("Text A", rank: 10);
        var high = await SeedAsync("Text B", rank: 90);
        var middle = await SeedAsync("Text C", rank: 50);
        var coordinator = Factory.Services.GetRequiredService<ExtractionCoordinator>();

        coordinator.RequestVacancy(low.Id);
        coordinator.RequestBackfill();
        await WaitIdleAsync();

        var order = _model.Requests.Select(r => r.User).ToList();
        order[0].Should().Contain("Text A", "the single request goes first");
        order[1].Should().Contain("Text B");
        order[2].Should().Contain("Text C");
        order.Should().HaveCount(3, "the single one is not analysed twice");
        var status = await Client.GetFromJsonAsync<JsonElement>("/api/extractions", CancellationToken);
        status.GetProperty("extracted").GetInt32().Should().Be(3);
        status.GetProperty("available").GetBoolean().Should().BeTrue();
        _ = high;
        _ = middle;
    }

    [Fact]
    public async Task Cancelling_stops_the_run_after_the_current_vacancy()
    {
        var release = new TaskCompletionSource();
        _model.Answer = _ => ValidJson;
        _model.Before = async ct => await release.Task.WaitAsync(ct);
        await SeedAsync("Text A", rank: 90);
        await SeedAsync("Text B", rank: 10);

        (await Client.PostAsync("/api/extractions", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await WaitUntilAsync(() => _model.Requests.Count == 1);
        (await Client.DeleteAsync("/api/extractions/current", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        release.SetResult();
        await WaitIdleAsync();

        _model.Requests.Should().HaveCount(1);
        var status = await Client.GetFromJsonAsync<JsonElement>("/api/extractions", CancellationToken);
        status.GetProperty("lastError").GetString().Should().Be("Abgebrochen.");
    }

    [Fact]
    public async Task A_model_that_is_down_stops_the_run_and_changes_nothing()
    {
        _model.Answer = _ => throw new LlmException(LlmError.Unavailable, "Ollama ist unter http://localhost:11434 nicht erreichbar.");
        var vacancy = await SeedAsync("Wir suchen C#-Entwickler.", rank: 90);
        await SeedAsync("Noch eine", rank: 10);

        (await Client.PostAsync("/api/extractions", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await WaitIdleAsync();

        _model.Requests.Should().HaveCount(1, "the run stops at the first unavailable call");
        (await ReloadAsync(vacancy.Id)).ExtractionKey.Should().BeNull("nothing cached when the model was not reached");
        var status = await Client.GetFromJsonAsync<JsonElement>("/api/extractions", CancellationToken);
        status.GetProperty("lastError").GetString().Should().Contain("nicht erreichbar");
    }

    [Fact]
    public async Task Without_a_usable_model_the_endpoints_say_so_and_nothing_is_queued()
    {
        _model.IsEnabled = false;
        var vacancy = await SeedAsync("Text");

        var single = await Client.PostAsync($"/api/vacancies/{vacancy.Id}/extract", null, CancellationToken);
        var all = await Client.PostAsync("/api/extractions", null, CancellationToken);

        single.StatusCode.Should().Be(HttpStatusCode.Conflict);
        all.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _model.Requests.Should().BeEmpty();
        (await Client.PostAsync($"/api/vacancies/{Guid.NewGuid()}/extract", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Gold_labels_are_stored_changed_counted_and_removed()
    {
        var a = await SeedAsync("A");
        var b = await SeedAsync("B");

        (await Client.PutAsJsonAsync($"/api/vacancies/{a.Id}/label", new { fits = true }, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await Client.PutAsJsonAsync($"/api/vacancies/{b.Id}/label", new { fits = true }, CancellationToken);
        await Client.PutAsJsonAsync($"/api/vacancies/{b.Id}/label", new { fits = false }, CancellationToken);

        var summary = await Client.GetFromJsonAsync<GoldLabelSummaryDto>("/api/labels/summary", CancellationToken);
        summary.Should().Be(new GoldLabelSummaryDto(2, 1, 1, ExtractionEndpoints.LabelTarget));
        (await Client.GetFromJsonAsync<JsonElement>($"/api/vacancies/{b.Id}", CancellationToken)).GetProperty("goldLabel").GetBoolean().Should().BeFalse();

        (await Client.DeleteAsync($"/api/vacancies/{a.Id}/label", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<GoldLabelSummaryDto>("/api/labels/summary", CancellationToken))!.Labeled.Should().Be(1);
        (await Client.PutAsJsonAsync($"/api/vacancies/{a.Id}/label", new { }, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PutAsJsonAsync($"/api/vacancies/{Guid.NewGuid()}/label", new { fits = true }, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task WaitIdleAsync()
    {
        var coordinator = Factory.Services.GetRequiredService<ExtractionCoordinator>();
        await WaitUntilAsync(() => coordinator.Status(true, "x").State == ExtractionState.Idle);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the background extraction should finish");
            await Task.Delay(20, CancellationToken);
        }
    }
}

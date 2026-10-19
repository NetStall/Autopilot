using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Import;
using JobAutopilot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Integration;

/// <summary>Background worker: status transitions, progress, cancellation on shutdown, interrupted runs.</summary>
public class ImportWorkerTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private readonly FakeVacancySource _source = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
    }

    [Fact]
    public async Task Run_goes_from_queued_to_running_with_progress_to_succeeded()
    {
        _source.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _source.Returns("first", Candidate("1")).Returns("second", Candidate("2"));

        var response = await PostImportAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = (await response.Content.ReadFromJsonAsync<ImportStartedDto>(ApiJson, CancellationToken))!;
        await _source.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

        var running = await GetImportAsync(started.ImportId);
        running.Should().BeEquivalentTo(new
        {
            Status = ImportRunStatus.Running,
            SearchesDone = 0,
            CurrentSearch = "region-softwareentwickler",
        });
        running.StartedAt.Should().NotBeNull();
        running.SearchesTotal.Should().BeGreaterThan(1);
        (await PostImportAsync()).StatusCode.Should().Be(HttpStatusCode.Conflict, "one import at a time");

        _source.Gate.SetResult();
        var finished = await WaitForImportAsync(started.ImportId);

        finished.Status.Should().Be(ImportRunStatus.Succeeded);
        finished.CurrentSearch.Should().BeNull();
        finished.SearchesDone.Should().Be(finished.SearchesTotal);
        finished.FinishedAt.Should().NotBeNull();
        (await PostImportAsync()).StatusCode.Should().Be(HttpStatusCode.Accepted, "the guard is released afterwards");
    }

    [Fact]
    public async Task Shutdown_cancels_a_running_import_and_writes_no_vacancies()
    {
        _source.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _source.Returns("region-softwareentwickler", Candidate("1"));
        var response = await PostImportAsync();
        var started = (await response.Content.ReadFromJsonAsync<ImportStartedDto>(ApiJson, CancellationToken))!;
        await _source.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

        await Factory.DisposeAsync(); // graceful host shutdown

        await using var db = Postgres.CreateDbContext();
        var run = await db.ImportRuns.SingleAsync(r => r.Id == started.ImportId, CancellationToken);
        run.Status.Should().Be(ImportRunStatus.Cancelled);
        run.IsActive.Should().BeFalse();
        run.FinishedAt.Should().NotBeNull();
        (await db.Vacancies.CountAsync(CancellationToken)).Should().Be(0, "nothing half-written");
    }

    [Fact]
    public async Task Runs_left_active_by_a_crashed_process_are_marked_failed_at_start()
    {
        await Factory.DisposeAsync();
        Guid staleId;
        await using (var db = Postgres.CreateDbContext())
        {
            var stale = ImportRun.Queue(ImportTrigger.Manual, 3, DateTimeOffset.UtcNow.AddHours(-1));
            stale.Start(DateTimeOffset.UtcNow.AddHours(-1));
            db.ImportRuns.Add(stale);
            await db.SaveChangesAsync(CancellationToken);
            staleId = stale.Id;
        }

        await using var restarted = new ApiFactory(Postgres, ConfigureServices);
        using var client = restarted.CreateClient();
        var run = await client.GetFromJsonAsync<ImportRunDto>($"/api/imports/{staleId}", ApiJson, CancellationToken);

        run!.Status.Should().Be(ImportRunStatus.Failed);
        run.Error.Should().StartWith("Interrupted");
        (await client.PostAsync("/api/imports", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}

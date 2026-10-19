using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Tests.Fakes;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Unit.Import;

public class ImportRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lifecycle_queued_running_succeeded()
    {
        var run = ImportRun.Queue(ImportTrigger.Scheduled, searchesTotal: 2, Now);
        run.Should().BeEquivalentTo(new { Status = ImportRunStatus.Queued, IsActive = true, RequestedAt = Now, SearchesTotal = 2 });

        run.Start(Now.AddSeconds(1));
        run.ReportProgress(new ImportProgress(1, 2, "remote-dotnet"));
        run.Should().BeEquivalentTo(new { Status = ImportRunStatus.Running, IsActive = true, SearchesDone = 1, CurrentSearch = "remote-dotnet" });

        var search = new VacancySearch { Name = "remote-dotnet", Keyword = ".NET", RemoteOnly = true };
        run.Succeed(new ImportResult(5, 2, 1, 1, 1, [new SearchImportResult(search, 5, 2, 1, 1, 1)]), Now.AddSeconds(9));

        run.Should().BeEquivalentTo(new
        {
            Status = ImportRunStatus.Succeeded,
            IsActive = false,
            SearchesDone = 2,
            CurrentSearch = (string?)null,
            Fetched = 5,
            Added = 2,
            Updated = 1,
            Unchanged = 1,
            Excluded = 1,
            FinishedAt = Now.AddSeconds(9),
            Error = (string?)null,
        });
        run.Searches.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "remote-dotnet", RemoteOnly = true, Added = 2 });
    }

    [Fact]
    public void Failed_and_cancelled_runs_are_no_longer_active()
    {
        var failed = ImportRun.Queue(ImportTrigger.Manual, 1, Now);
        failed.Fail("API down", Now);
        var cancelled = ImportRun.Queue(ImportTrigger.Manual, 1, Now);
        cancelled.Start(Now);
        cancelled.Cancel(Now);

        failed.Should().BeEquivalentTo(new { Status = ImportRunStatus.Failed, IsActive = false, Error = "API down" });
        cancelled.Status.Should().Be(ImportRunStatus.Cancelled);
        cancelled.IsActive.Should().BeFalse();
        cancelled.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task Import_service_reports_progress_before_each_search()
    {
        var source = new FakeVacancySource().Returns("a", Candidate("1")).Returns("b", Candidate("2"));
        var reports = new List<ImportProgress>();
        var searches = new[]
        {
            new VacancySearch { Name = "a", Keyword = "A" },
            new VacancySearch { Name = "b", Keyword = "B" },
        };

        await new VacancyImportService(new InMemoryVacancyStore(), TimeProvider.System).ImportAsync(
            source, searches, TitleExclusionFilter.None, TestContext.Current.CancellationToken,
            (progress, _) =>
            {
                reports.Add(progress);
                return Task.CompletedTask;
            });

        reports.Should().Equal(new ImportProgress(0, 2, "a"), new ImportProgress(1, 2, "b"));
    }

    [Fact]
    public async Task Cancelled_import_saves_nothing()
    {
        var store = new InMemoryVacancyStore();
        var source = new FakeVacancySource().Returns("a", Candidate("1"));
        using var cts = new CancellationTokenSource();

        var act = () => new VacancyImportService(store, TimeProvider.System).ImportAsync(
            source, [new VacancySearch { Name = "a", Keyword = "A" }], TitleExclusionFilter.None, cts.Token,
            async (_, _) => await cts.CancelAsync());

        await act.Should().ThrowAsync<OperationCanceledException>();
        store.SaveCount.Should().Be(0);
        store.Saved.Should().BeEmpty();
    }
}

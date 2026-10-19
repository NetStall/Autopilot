using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>
/// Runs queued imports one after another in the background and records status and progress in <c>ImportRuns</c>.
/// On shutdown a running import is cancelled; it writes vacancies only at its very end (one transaction), so a
/// cancelled run leaves nothing half-written and is marked <see cref="ImportRunStatus.Cancelled"/>.
/// </summary>
public sealed partial class ImportWorker(
    ImportJobChannel channel,
    IServiceScopeFactory scopes,
    IImportLock importLock,
    TimeProvider timeProvider,
    ILogger<ImportWorker> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Before the server accepts requests: runs left "active" by a previous process that died.
        await FailInterruptedRunsAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in channel.Reader.ReadAllAsync(stoppingToken))
            {
                await RunAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    internal async Task RunAsync(ImportJob job, CancellationToken stoppingToken)
    {
        await using var statusScope = scopes.CreateAsyncScope();
        var runs = statusScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await runs.ImportRuns.SingleOrDefaultAsync(r => r.Id == job.RunId, CancellationToken.None);
        if (run is null)
        {
            return;
        }

        try
        {
            await using var lockHandle = await importLock.TryAcquireAsync(stoppingToken);
            if (lockHandle is null)
            {
                // Another API process (e.g. dev.ps1 next to docker compose) is importing into the same database.
                run.Fail("Another import is running against the same database.", timeProvider.GetUtcNow());
                return;
            }

            run.Start(timeProvider.GetUtcNow());
            await runs.SaveChangesAsync(stoppingToken);
            LogStarted(logger, run.Id, run.Trigger, job.Searches.Count);

            await using var importScope = scopes.CreateAsyncScope();
            var services = importScope.ServiceProvider;
            var source = services.GetServices<IVacancySource>().Single(s => s.Source == VacancySource.Arbeitsagentur);
            var result = await services.GetRequiredService<VacancyImportService>().ImportAsync(
                source,
                job.Searches,
                new TitleExclusionFilter(job.ExcludeTitleTerms),
                stoppingToken,
                async (progress, ct) =>
                {
                    run.ReportProgress(progress);
                    await runs.SaveChangesAsync(ct);
                });

            run.Succeed(result, timeProvider.GetUtcNow());
            foreach (var search in result.Searches)
            {
                LogSearchImported(logger, search.Search.Name, search.Fetched, search.Added, search.Updated,
                    search.Unchanged, search.Excluded);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            run.Cancel(timeProvider.GetUtcNow());
            LogCancelled(logger, run.Id);
        }
        catch (VacancySourceUnavailableException ex)
        {
            run.Fail("The Arbeitsagentur Jobsuche API is not reachable. Nothing was imported. " + ex.Message,
                timeProvider.GetUtcNow());
            LogFailed(logger, ex, run.Id);
        }
#pragma warning disable CA1031 // The worker must survive any failure of a single run and record it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            run.Fail("Unexpected error. Nothing was imported; see the API log.", timeProvider.GetUtcNow());
            LogFailed(logger, ex, run.Id);
        }
        finally
        {
            await runs.SaveChangesAsync(CancellationToken.None);
            LogFinished(logger, run.Id, run.Status, run.Added, run.Updated, run.Unchanged, run.Excluded);
        }
    }

    private async Task FailInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        // Only when no other process is importing right now — otherwise its active run is not stale.
        await using var lockHandle = await importLock.TryAcquireAsync(cancellationToken);
        if (lockHandle is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await db.ImportRuns.Where(r => r.IsActive).ToListAsync(cancellationToken);
        foreach (var run in stale)
        {
            run.Fail("Interrupted: the API was stopped while this import was queued or running.", timeProvider.GetUtcNow());
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            LogInterrupted(logger, stale.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import {RunId} ({Trigger}) started with {SearchCount} searches")]
    private static partial void LogStarted(ILogger logger, Guid runId, ImportTrigger trigger, int searchCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Imported search {SearchName}: fetched {Fetched}, added {Added}, updated {Updated}, unchanged {Unchanged}, excluded {Excluded}")]
    private static partial void LogSearchImported(
        ILogger logger, string searchName, int fetched, int added, int updated, int unchanged, int excluded);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Import {RunId} finished: {Status}, added {Added}, updated {Updated}, unchanged {Unchanged}, excluded {Excluded}")]
    private static partial void LogFinished(
        ILogger logger, Guid runId, ImportRunStatus status, int added, int updated, int unchanged, int excluded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {RunId} was cancelled because the API is shutting down")]
    private static partial void LogCancelled(ILogger logger, Guid runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {RunId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marked {Count} interrupted import run(s) as failed")]
    private static partial void LogInterrupted(ILogger logger, int count);
}

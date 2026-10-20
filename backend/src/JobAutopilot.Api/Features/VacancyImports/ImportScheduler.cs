using JobAutopilot.Core.Import;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>
/// Daily automatic import (<c>Import:Schedule</c>). Uses the same queue and single-import guard as the button and
/// only fetches vacancies — it never sends anything anywhere (AGENTS.md). If the app was not running at the
/// scheduled time (laptop off at 07:00), the missed run is caught up once at start-up.
/// </summary>
public sealed partial class ImportScheduler(
    IOptions<ImportOptions> options,
    ImportCoordinator coordinator,
    Core.SearchProfiles.ISearchProfileSource searchProfiles,
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<ImportScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Schedule.Enabled)
        {
            return;
        }

        var schedule = settings.Schedule.ToSchedule();
        try
        {
            if (schedule.IsDue(timeProvider.GetUtcNow(), await LastRequestedAtAsync(stoppingToken)))
            {
                await StartAsync("catch-up after start", stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = timeProvider.GetUtcNow();
                var next = schedule.NextAfter(now);
                LogNextRun(logger, next);
                await Task.Delay(next - now, timeProvider, stoppingToken);
                await StartAsync("daily schedule", stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task StartAsync(string reason, CancellationToken cancellationToken)
    {
        var (searches, exclusions) = SearchProfiles.SearchProfileImports.Current(searchProfiles);
        var run = await coordinator.TryStartAsync(ImportTrigger.Scheduled, searches, exclusions, cancellationToken);
        if (run is null)
        {
            LogSkipped(logger, reason);
        }
        else
        {
            LogQueued(logger, run.Id, reason);
        }
    }

    private async Task<DateTimeOffset?> LastRequestedAtAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportRuns.MaxAsync(r => (DateTimeOffset?)r.RequestedAt, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Next scheduled import at {NextRun}")]
    private static partial void LogNextRun(ILogger logger, DateTimeOffset nextRun);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled import {RunId} queued ({Reason})")]
    private static partial void LogQueued(ILogger logger, Guid runId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled import skipped ({Reason}): another import is queued or running")]
    private static partial void LogSkipped(ILogger logger, string reason);
}

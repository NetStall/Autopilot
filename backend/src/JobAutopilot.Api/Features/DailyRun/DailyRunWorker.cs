using System.Diagnostics;
using JobAutopilot.Api.Features.Extraction;
using JobAutopilot.Api.Features.Scoring;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Filters;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.DailyRun;

/// <summary>
/// „Tageslauf" (08b): import (all sources) → hard filters → rule scoring → Qwen extraction + KI score for new or
/// changed vacancies in the Top N → application channel → summary. Reuses the import, re-score and extraction
/// workers and waits for each; idempotent (the import de-duplicates, the extraction is cached). Without the local model
/// the KI stage is skipped with a message and the rest runs. Nothing is ever sent.
/// </summary>
public sealed partial class DailyRunWorker(
    DailyRunCoordinator coordinator,
    IServiceScopeFactory scopes,
    ImportCoordinator imports,
    RescoreCoordinator rescore,
    ExtractionCoordinator extraction,
    ILocalModelProbe probe,
    TimeProvider timeProvider,
    ILogger<DailyRunWorker> logger) : BackgroundService
{
    /// <summary>Polling interval while waiting for another worker.</summary>
    internal static TimeSpan Poll { get; set; } = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var top in coordinator.Requests.ReadAllAsync(stoppingToken))
        {
            var token = coordinator.Started(stoppingToken);
            DailyRunSummaryDto? summary = null;
            try
            {
                summary = await RunAsync(top, token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                coordinator.StageDone(new DailyRunStageDto(DailyRunStage.Summary, StageOutcome.Cancelled, "Abgebrochen.", 0));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
                coordinator.StageDone(new DailyRunStageDto(DailyRunStage.Summary, StageOutcome.Failed, $"Tageslauf abgebrochen: {ex.Message}", 0));
            }
            finally
            {
                coordinator.Finished(summary);
            }
        }
    }

    private async Task<DailyRunSummaryDto> RunAsync(int top, CancellationToken ct)
    {
        var startedAt = timeProvider.GetUtcNow();
        var failures = new List<string>();
        var stages = new List<DailyRunStageDto>();
        var before = (await TopAsync(20, ct)).Select(v => v.Id).ToHashSet();

        async Task Stage(DailyRunStage stage, Func<Task<(StageOutcome Outcome, string Message)>> run)
        {
            coordinator.Stage(stage);
            var watch = Stopwatch.StartNew();
            var (outcome, message) = await run();
            var result = new DailyRunStageDto(stage, outcome, message, watch.ElapsedMilliseconds);
            stages.Add(result);
            coordinator.StageDone(result);
            if (outcome == StageOutcome.Failed)
            {
                failures.Add($"{stage}: {message}");
            }
        }

        // 1. Import of all configured searches (or wait for the one already running).
        var imported = 0;
        await Stage(DailyRunStage.Import, async () =>
        {
            Guid runId;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var (searches, exclusions) = SearchProfiles.SearchProfileImports.Current(scope.ServiceProvider.GetRequiredService<ISearchProfileSource>());
                var queued = await imports.TryStartAsync(ImportTrigger.DailyRun, searches, exclusions, ct);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                runId = queued?.Id ?? await db.ImportRuns.Where(r => r.IsActive).Select(r => r.Id).FirstOrDefaultAsync(ct);
            }

            var run = await WaitForImportAsync(runId, ct);
            if (run is null)
            {
                return (StageOutcome.Failed, "Kein Import gefunden.");
            }

            imported = run.Added;
            var failedSearches = run.Searches.Count(s => s.Error is not null);
            var outcome = run.Status == ImportRunStatus.Succeeded ? StageOutcome.Done : StageOutcome.Failed;
            return (outcome, $"{run.Fetched} geladen, {run.Added} neu, {run.Updated} geändert" + (failedSearches > 0 ? $", {failedSearches} Suchen mit Fehler" : string.Empty)
                + (run.Error is { } error ? $" – {error}" : string.Empty));
        });

        // 2. + 3. Hard filters and rule scoring happen in one re-score of all vacancies.
        var scoringWatch = Stopwatch.StartNew();
        var newFiltered = new Dictionary<HardFilterReason, int>();
        await Stage(DailyRunStage.Filters, async () =>
        {
            var requestedAt = timeProvider.GetUtcNow();
            rescore.Request();
            await WaitAsync(() => rescore.Status is { State: RescoreState.Idle } s && s.LastFinishedAt >= requestedAt, ct);
            newFiltered = await NewFilteredAsync(startedAt, ct);
            return (StageOutcome.Done,
                newFiltered.Count == 0 ? "Keine neue Stelle gefiltert." : "Neu gefiltert: " + string.Join(", ", newFiltered.Select(f => $"{HardFilters.Label(f.Key)} {f.Value}")));
        });
        await Stage(DailyRunStage.Scoring, () => Task.FromResult(rescore.Status.LastError is { } error
            ? (StageOutcome.Failed, error)
            : (StageOutcome.Done, $"{rescore.Status.LastScored ?? 0} Stellen bewertet in {scoringWatch.Elapsed.TotalSeconds:0.#} s")));

        // 4. Qwen extraction + KI score for the new or changed ones in the Top N (cached otherwise).
        var (reachable, probeError) = await probe.ProbeAsync(ct);
        await Stage(DailyRunStage.Extraction, async () =>
        {
            if (!reachable)
            {
                return (StageOutcome.Skipped, $"Lokales Modell nicht erreichbar – KI-Analyse übersprungen ({probeError ?? "keine Antwort"}).");
            }

            extraction.RequestBackfill(top);
            await WaitAsync(() => extraction.Status(true, string.Empty).State == ExtractionState.Idle, ct);
            var status = extraction.Status(true, string.Empty);
            return status.LastError is { } error
                ? (StageOutcome.Failed, error)
                : (StageOutcome.Done, $"Top {top}: {status.Extracted} neu analysiert, {status.Cached} aus dem Cache, {status.Failed} ungültig");
        });

        // 5. Application channel of the Top 20 (rules; the model's part only when it ran).
        var after = await TopAsync(20, ct);
        var ready = new List<ReadyInTopDto>();
        await Stage(DailyRunStage.Channels, () =>
        {
            foreach (var v in after)
            {
                var ai = reachable ? StoredExtraction.ForDescription(v.ExtractionJson, v.Description)?.Result.Application : null;
                var how = HowToApplyDetector.Resolve(v.Description, v.Url, ai);
                if (how.EmailReady(!string.IsNullOrWhiteSpace(v.Description)))
                {
                    ready.Add(new ReadyInTopDto(v.Id, v.Title, v.Company, how.Email!, how.ContactName));
                }
            }

            return Task.FromResult((StageOutcome.Done, $"{ready.Count} von {after.Count} der Top 20 per E-Mail vorbereitbar" + (reachable ? string.Empty : " (nur Regeln)")));
        });

        var newInTop = after.Where(v => !before.Contains(v.Id)).Select(v => new NewInTopDto(v.Id, v.Title, v.Company, v.RankScore)).ToList();
        await Stage(DailyRunStage.Summary, () => Task.FromResult((StageOutcome.Done,
            $"{imported} neue Stellen, {newInTop.Count} neu in den Top 20, {ready.Count} per E-Mail bereit, {failures.Count} Fehler")));
        return new DailyRunSummaryDto(startedAt, timeProvider.GetUtcNow(), imported, newFiltered, newInTop, ready, failures, stages);
    }

    private sealed record TopRow(Guid Id, string Title, string Company, int? RankScore, string? Description, string? Url, string? ExtractionJson);

    /// <summary>The list as I see it: current, visible, by rank.</summary>
    private async Task<List<TopRow>> TopAsync(int n, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = scope.ServiceProvider.GetRequiredService<ISearchProfileSource>().Current.Profile;
        var filter = new VacancyFilter(NotExpiredAt: timeProvider.GetUtcNow(), HiddenEmployerTypes: profile.HiddenEmployerTypes(), HideFiltered: true);
        return await VacancyEndpoints.Filtered(db, filter).ApplySort(VacancySort.Relevance).Take(n)
            .Select(v => new TopRow(v.Id, v.Title, v.Company, v.RankScore, v.Description, v.Url, v.ExtractionJson))
            .ToListAsync(ct);
    }

    private async Task<Dictionary<HardFilterReason, int>> NewFilteredAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Vacancies.AsNoTracking().Where(v => v.FetchedAt >= since && v.FilterReason != null)
            .GroupBy(v => v.FilterReason!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => r.Count);
    }

    private async Task<ImportRun?> WaitForImportAsync(Guid runId, CancellationToken ct)
    {
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.ImportRuns.AsNoTracking().Include(r => r.Searches).FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null || !run.IsActive)
            {
                return run;
            }

            await Task.Delay(Poll, ct);
        }
    }

    private static async Task WaitAsync(Func<bool> done, CancellationToken ct)
    {
        while (!done())
        {
            await Task.Delay(Poll, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Daily run failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

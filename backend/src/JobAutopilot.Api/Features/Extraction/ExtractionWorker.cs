using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.Extraction;

/// <summary>
/// Runs requirement extractions one after another in the background (one GPU job at a time): queued single vacancies
/// first, then the pending ones of a backfill, most relevant first, at most <c>Llm:Extraction:MaxPerRun</c>. When the
/// provider is down or not set up, the run stops with that error and the rules stay in charge — nothing breaks.
/// </summary>
public sealed partial class ExtractionWorker(
    ExtractionCoordinator coordinator,
    IServiceScopeFactory scopes,
    IOptions<VacancyExtractionOptions> options,
    ILogger<ExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await coordinator.Signals.WaitToReadAsync(stoppingToken))
            {
                coordinator.Signals.TryRead(out _);
                await RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: what was extracted is saved per vacancy.
        }
    }

    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        var token = coordinator.Start(stoppingToken);
        string? error = null;
        try
        {
            await using (var scope = scopes.CreateAsyncScope())
            {
                if (!scope.ServiceProvider.GetRequiredService<VacancyExtractionService>().IsAvailable)
                {
                    error = "KI-Analyse nicht eingerichtet (Llm:Tasks:Extraction, Ollama ausgeschaltet oder kein Schlüssel) – es gelten die Regeln.";
                    coordinator.Cancel();
                    return;
                }
            }

            var backfill = new Queue<Guid>();
            while (!token.IsCancellationRequested)
            {
                Guid? next = coordinator.TakeSingle();
                if (next is { } single)
                {
                    coordinator.AddTotal(1);
                }
                else
                {
                    if (coordinator.TakeBackfill() is { } top)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var n = Math.Min(top, options.Value.MaxPerRun);
                        var pending = (await scope.ServiceProvider.GetRequiredService<VacancyExtractionService>().PendingAsync(n, token))
                            .Union(await scope.ServiceProvider.GetRequiredService<VacancyFitScoreService>().PendingAsync(n, token))
                            .ToList();
                        foreach (var id in pending.Where(id => !backfill.Contains(id)))
                        {
                            backfill.Enqueue(id);
                        }

                        coordinator.AddTotal(pending.Count);
                    }

                    if (!backfill.TryDequeue(out var queued))
                    {
                        break;
                    }

                    next = queued;
                }

                coordinator.Begin(next.Value);
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var outcome = await scope.ServiceProvider.GetRequiredService<VacancyExtractionService>().ExtractAsync(next.Value, token);

                    // „KI-Bewertung" in the same GPU queue, right after the extraction (cached, so usually only new ones).
                    var fitScore = scope.ServiceProvider.GetRequiredService<VacancyFitScoreService>();
                    if (fitScore.IsAvailable)
                    {
                        await fitScore.ScoreAsync(next.Value, token);
                    }

                    coordinator.Completed(outcome);
                }
            }

            if (token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                error = "Abgebrochen.";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            error = "Abgebrochen.";
        }
        catch (LlmException ex)
        {
            // Ollama down, model not pulled …: stop the run, keep the rules; queued single vacancies are dropped too.
            error = ex.Message;
            coordinator.Cancel();
            LogStopped(logger, ex.Error);
        }
        finally
        {
            coordinator.Finish(error);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Extraction run stopped: {Error}")]
    private static partial void LogStopped(ILogger logger, LlmError error);
}

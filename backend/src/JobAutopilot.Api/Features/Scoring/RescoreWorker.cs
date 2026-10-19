using JobAutopilot.Core.Import;
using JobAutopilot.Core.Scoring;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.Scoring;

/// <summary>
/// Re-scores all vacancies in the background: on request (UI „Neu bewerten") and once at start-up
/// (<c>Relevance:RescoreOnStartup</c>), so changed weights or phrases apply to everything. Waits while an import holds
/// the import lock, so the two never write the same vacancies at the same time.
/// </summary>
public sealed partial class RescoreWorker(
    RescoreCoordinator coordinator,
    IServiceScopeFactory scopes,
    IImportLock importLock,
    IOptions<RelevanceOptions> options,
    TimeProvider timeProvider,
    ILogger<RescoreWorker> logger) : BackgroundService
{
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.RescoreOnStartup)
        {
            coordinator.Request();
        }

        try
        {
            await foreach (var _ in coordinator.Requests.ReadAllAsync(stoppingToken))
            {
                await RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        await using var lockHandle = await AcquireLockAsync(stoppingToken);
        coordinator.MarkRunning();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scored = await scope.ServiceProvider.GetRequiredService<VacancyRescoreService>().RescoreAllAsync(stoppingToken);
            coordinator.MarkFinished(scored, error: null);
            LogRescored(logger, scored);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            coordinator.MarkFinished(null, "Cancelled: the API was shutting down.");
            throw;
        }
#pragma warning disable CA1031 // The worker must survive a failed run and report it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            coordinator.MarkFinished(null, "Re-scoring failed; see the API log.");
            LogFailed(logger, ex);
        }
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            if (await importLock.TryAcquireAsync(stoppingToken) is { } handle)
            {
                return handle;
            }

            await Task.Delay(LockRetryDelay, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-scored {Count} vacancies")]
    private static partial void LogRescored(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-scoring failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

using System.Threading.Channels;
using JobAutopilot.Core.Filters;
using JobAutopilot.Infrastructure.Llm;

namespace JobAutopilot.Api.Features.DailyRun;

public enum DailyRunState
{
    Idle,
    Queued,
    Running,
}

public enum DailyRunStage
{
    Import,
    Filters,
    Scoring,
    Extraction,
    Channels,
    Summary,
}

public enum StageOutcome
{
    Done,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>One finished stage: what happened, in a sentence, and how long it took.</summary>
public sealed record DailyRunStageDto(DailyRunStage Stage, StageOutcome Outcome, string Message, long DurationMs);

public sealed record NewInTopDto(Guid VacancyId, string Title, string Company, int? RankScore);

public sealed record ReadyInTopDto(Guid VacancyId, string Title, string Company, string Email, string? ContactName);

/// <summary>The card after a run: new vacancies, filtered ones by reason, newly in Top 20, e-mail-ready in Top 20, failures.</summary>
public sealed record DailyRunSummaryDto(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int NewVacancies,
    IReadOnlyDictionary<HardFilterReason, int> NewFiltered,
    IReadOnlyList<NewInTopDto> NewInTop20,
    IReadOnlyList<ReadyInTopDto> EmailReadyInTop20,
    IReadOnlyList<string> Failures,
    IReadOnlyList<DailyRunStageDto> Stages);

public sealed record DailyRunStatusDto(DailyRunState State, DailyRunStage? CurrentStage, IReadOnlyList<DailyRunStageDto> Stages, DailyRunSummaryDto? Last);

/// <summary>Whether the local model answers right now — the daily run skips the KI stages when it does not.</summary>
public interface ILocalModelProbe
{
    Task<(bool Reachable, string? Error)> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary><see cref="ILocalModelProbe"/> over Ollama's status endpoint.</summary>
public sealed class OllamaModelProbe(OllamaLlmClient ollama) : ILocalModelProbe
{
    public async Task<(bool Reachable, string? Error)> ProbeAsync(CancellationToken cancellationToken)
    {
        var status = await ollama.GetStatusAsync(cancellationToken);
        return (status.Enabled && status.Reachable, status.Error ?? (status.Enabled ? null : "Ollama ist ausgeschaltet (Llm:Providers:Ollama:Enabled)."));
    }
}

/// <summary>
/// „Tageslauf" (08b): one run at a time, more requests while one is queued or running are merged; progress and the last
/// summary for the UI, cancel for the running one. The <see cref="DailyRunWorker"/> executes it.
/// </summary>
public sealed class DailyRunCoordinator
{
    private readonly Channel<int> _requests = Channel.CreateBounded<int>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly Lock _lock = new();
    private DailyRunState _state = DailyRunState.Idle;
    private DailyRunStage? _stage;
    private List<DailyRunStageDto> _stages = [];
    private DailyRunSummaryDto? _last;
    private CancellationTokenSource? _cancel;

    internal ChannelReader<int> Requests => _requests.Reader;

    /// <returns><c>false</c> when a run is already queued or running (the request is merged into it).</returns>
    public bool Request(int top)
    {
        lock (_lock)
        {
            if (_state != DailyRunState.Idle)
            {
                return false;
            }

            _requests.Writer.TryWrite(top);
            (_state, _stages) = (DailyRunState.Queued, []);
            return true;
        }
    }

    public DailyRunStatusDto Status
    {
        get
        {
            lock (_lock)
            {
                return new DailyRunStatusDto(_state, _stage, [.. _stages], _last);
            }
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cancel?.Cancel();
        }
    }

    internal CancellationToken Started(CancellationToken stopping)
    {
        lock (_lock)
        {
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            _state = DailyRunState.Running;
            return _cancel.Token;
        }
    }

    internal void Stage(DailyRunStage stage)
    {
        lock (_lock)
        {
            _stage = stage;
        }
    }

    internal void StageDone(DailyRunStageDto result)
    {
        lock (_lock)
        {
            _stages.Add(result);
        }
    }

    internal void Finished(DailyRunSummaryDto? summary)
    {
        lock (_lock)
        {
            _last = summary ?? _last;
            (_state, _stage) = (DailyRunState.Idle, null);
            _cancel?.Dispose();
            _cancel = null;
        }
    }
}

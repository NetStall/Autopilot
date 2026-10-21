using System.Threading.Channels;

namespace JobAutopilot.Api.Features.Extraction;

public enum ExtractionState
{
    Idle,
    Queued,
    Running,
}

/// <summary>State of the background extraction, for <c>GET /api/extractions</c>.</summary>
public sealed record ExtractionStatusDto(
    ExtractionState State,
    bool Available,
    string Model,
    int Total,
    int Done,
    int Extracted,
    int Cached,
    int Failed,
    Guid? CurrentVacancyId,
    IReadOnlyList<Guid> Queued,
    double? SecondsPerVacancy,
    double? TokensPerSecond,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? LastError);

/// <summary>
/// The queue of the <see cref="ExtractionWorker"/>: single vacancies („Mit KI analysieren") go first, then a backfill
/// of all pending ones (after an import or on request). One GPU job at a time; <see cref="Cancel"/> stops the current
/// run after the vacancy being analysed.
/// </summary>
public sealed class ExtractionCoordinator(TimeProvider timeProvider)
{
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly Lock _lock = new();
    private readonly List<Guid> _single = [];
    private bool _backfill;
    private CancellationTokenSource? _run;
    private Progress _progress = new();
    private Progress _previous = new();

    internal ChannelReader<bool> Signals => _signal.Reader;

    /// <summary>Analyse all pending vacancies (most relevant first).</summary>
    public void RequestBackfill()
    {
        lock (_lock)
        {
            _backfill = true;
            _signal.Writer.TryWrite(true);
        }
    }

    /// <summary>Analyse this vacancy next, before the backfill continues.</summary>
    public void RequestVacancy(Guid vacancyId)
    {
        lock (_lock)
        {
            if (!_single.Contains(vacancyId))
            {
                _single.Add(vacancyId);
            }

            _signal.Writer.TryWrite(true);
        }
    }

    /// <summary>Drops everything queued and stops the running pass after the current vacancy.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _single.Clear();
            _backfill = false;
            _run?.Cancel();
        }
    }

    public ExtractionStatusDto Status(bool available, string model)
    {
        lock (_lock)
        {
            var p = _progress;
            var state = p.Running ? ExtractionState.Running : _single.Count > 0 || _backfill ? ExtractionState.Queued : ExtractionState.Idle;
            var analysed = p.Extracted;
            return new ExtractionStatusDto(
                state, available, model, p.Total, p.Done, p.Extracted, p.Cached, p.Failed, p.Current, [.. _single],
                analysed > 0 ? Math.Round(p.ModelMs / 1000.0 / analysed, 1) : null,
                p.ModelMs > 0 ? Math.Round(p.OutputTokens / (p.ModelMs / 1000.0), 1) : null,
                p.StartedAt, p.FinishedAt, p.LastError);
        }
    }

    /// <summary>Next single vacancy, if any.</summary>
    internal Guid? TakeSingle()
    {
        lock (_lock)
        {
            if (_single.Count == 0)
            {
                return null;
            }

            var next = _single[0];
            _single.RemoveAt(0);
            return next;
        }
    }

    internal bool TakeBackfill()
    {
        lock (_lock)
        {
            var requested = _backfill;
            _backfill = false;
            return requested;
        }
    }

    internal CancellationToken Start(CancellationToken stoppingToken)
    {
        lock (_lock)
        {
            _run?.Dispose();
            _run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _previous = _progress;
            _progress = new Progress { Running = true, StartedAt = timeProvider.GetUtcNow() };
            return _run.Token;
        }
    }

    internal void AddTotal(int count)
    {
        lock (_lock)
        {
            _progress.Total += count;
        }
    }

    internal void Begin(Guid vacancyId)
    {
        lock (_lock)
        {
            _progress.Current = vacancyId;
        }
    }

    internal void Completed(Infrastructure.Llm.ExtractionOutcome outcome)
    {
        lock (_lock)
        {
            _progress.Done++;
            _progress.Current = null;
            switch (outcome.Kind)
            {
                case Infrastructure.Llm.ExtractionOutcomeKind.Extracted:
                    _progress.Extracted++;
                    _progress.ModelMs += outcome.DurationMs;
                    _progress.OutputTokens += outcome.OutputTokens;
                    break;
                case Infrastructure.Llm.ExtractionOutcomeKind.Cached:
                    _progress.Cached++;
                    break;
                case Infrastructure.Llm.ExtractionOutcomeKind.Failed:
                    _progress.Failed++;
                    break;
            }
        }
    }

    internal void Finish(string? error)
    {
        lock (_lock)
        {
            if (error is null && _progress.Total == 0)
            {
                // Nothing to do (e.g. a second signal after the backfill was taken): keep showing the last real run.
                _progress = _previous;
                _progress.Running = false;
                return;
            }

            _progress.Running = false;
            _progress.Current = null;
            _progress.FinishedAt = timeProvider.GetUtcNow();
            _progress.LastError = error;
        }
    }

    private sealed class Progress
    {
        public bool Running { get; set; }

        public int Total { get; set; }

        public int Done { get; set; }

        public int Extracted { get; set; }

        public int Cached { get; set; }

        public int Failed { get; set; }

        public long ModelMs { get; set; }

        public long OutputTokens { get; set; }

        public Guid? Current { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public string? LastError { get; set; }
    }
}

using System.Threading.Channels;

namespace JobAutopilot.Api.Features.Scoring;

public enum RescoreState
{
    Idle,
    Queued,
    Running,
}

/// <summary>State of the background re-scoring, for <c>GET /api/vacancies/rescore</c>.</summary>
public sealed record RescoreStatus(
    RescoreState State,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastFinishedAt,
    int? LastScored,
    string? LastError);

/// <summary>
/// Queue of at most one pending re-score (more requests while one is pending are merged) plus its status.
/// The <see cref="RescoreWorker"/> executes it.
/// </summary>
public sealed class RescoreCoordinator(TimeProvider timeProvider)
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly Lock _lock = new();
    private RescoreStatus _status = new(RescoreState.Idle, null, null, null, null);

    public RescoreStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    internal ChannelReader<bool> Requests => _requests.Reader;

    /// <summary>Queues a re-score unless one is already waiting; returns the current status.</summary>
    public RescoreStatus Request()
    {
        lock (_lock)
        {
            _requests.Writer.TryWrite(true);
            if (_status.State == RescoreState.Idle)
            {
                _status = _status with { State = RescoreState.Queued };
            }

            return _status;
        }
    }

    internal void MarkRunning()
    {
        lock (_lock)
        {
            _status = _status with { State = RescoreState.Running, LastStartedAt = timeProvider.GetUtcNow() };
        }
    }

    internal void MarkFinished(int? scored, string? error)
    {
        lock (_lock)
        {
            // A request that arrived while running is already waiting in the channel.
            var next = _requests.Reader.Count > 0 ? RescoreState.Queued : RescoreState.Idle;
            _status = _status with
            {
                State = next,
                LastFinishedAt = timeProvider.GetUtcNow(),
                LastScored = scored,
                LastError = error,
            };
        }
    }
}

using System.Threading.Channels;
using JobAutopilot.Core.Import;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>What the worker needs to run one queued import.</summary>
public sealed record ImportJob(Guid RunId, IReadOnlyList<VacancySearch> Searches, IReadOnlyList<string> ExcludeTitleTerms);

/// <summary>In-process queue between the endpoints / the scheduler and the <see cref="ImportWorker"/>.</summary>
public sealed class ImportJobChannel
{
    private readonly Channel<ImportJob> _channel = Channel.CreateUnbounded<ImportJob>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<ImportJob> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(ImportJob job, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(job, cancellationToken);
}

/// <summary>
/// Starts imports for the UI and the scheduler. At most one import is queued or running at a time: the
/// <c>ImportRuns</c> table allows only one active row (filtered unique index), and the worker additionally holds the
/// PostgreSQL advisory lock while it runs (see ADR 0005/0006).
/// </summary>
public sealed class ImportCoordinator(IServiceScopeFactory scopes, ImportJobChannel channel, TimeProvider timeProvider)
{
    /// <summary>Returns the queued run, or <c>null</c> when another import is already queued or running.</summary>
    public async Task<ImportRun?> TryStartAsync(
        ImportTrigger trigger,
        IReadOnlyList<VacancySearch> searches,
        IReadOnlyList<string> excludeTitleTerms,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searches);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.ImportRuns.AnyAsync(r => r.IsActive, cancellationToken))
        {
            return null;
        }

        var run = ImportRun.Queue(trigger, searches.Count, timeProvider.GetUtcNow());
        db.ImportRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return null; // lost the race against a concurrent start
        }

        await channel.EnqueueAsync(new ImportJob(run.Id, searches, excludeTitleTerms), cancellationToken);
        return run;
    }
}

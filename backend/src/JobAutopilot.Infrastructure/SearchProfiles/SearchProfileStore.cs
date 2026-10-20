using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobAutopilot.Infrastructure.SearchProfiles;

/// <summary>The one Suchprofil row: a typed document as jsonb with a version for optimistic concurrency.</summary>
public class SearchProfileRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string Document { get; set; } = "{}";

    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A saved version of the Suchprofil (for history and rollback).</summary>
public class SearchProfileRevision
{
    public Guid Id { get; set; }

    public int Version { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public string Document { get; set; } = "{}";

    /// <summary>What changed, in German („Mindestbewertung 50 → 60").</summary>
    public string Summary { get; set; } = string.Empty;
}

public sealed record SearchProfileRevisionInfo(int Version, DateTimeOffset SavedAt, string Summary);

public sealed class SearchProfileValidationException(IReadOnlyList<string> errors)
    : Exception("The Suchprofil is invalid: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class SearchProfileConflictException(int currentVersion)
    : Exception($"The Suchprofil was changed in the meantime (now version {currentVersion}).")
{
    public int CurrentVersion { get; } = currentVersion;
}

/// <summary>
/// Loads, caches and saves the Suchprofil. On first start it is seeded (configuration defaults + the deprecated
/// <c>preferences</c> of <c>profile.json</c>). Every save is validated, checked against the expected version (409 on a
/// conflict), and recorded as a revision; the last <see cref="KeptRevisions"/> revisions are kept.
/// </summary>
public sealed partial class SearchProfileStore(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<SearchProfileStore> logger,
    Func<SearchProfile> seed) : ISearchProfileSource, IDisposable
{
    public const int KeptRevisions = 50;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        WriteIndented = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SearchProfileSnapshot? _current;

    /// <summary>The cached Suchprofil; before <see cref="InitializeAsync"/> the seed, not yet saved (version 0).</summary>
    public SearchProfileSnapshot Current => _current ??= new SearchProfileSnapshot(seed(), 0, null);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.SearchProfiles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == SearchProfileRecord.SingletonId, cancellationToken);
        if (record is not null)
        {
            _current = new SearchProfileSnapshot(Deserialize(record.Document), record.Version, record.UpdatedAt);
            return;
        }

        var profile = seed();
        var now = timeProvider.GetUtcNow();
        var json = Serialize(profile);
        db.SearchProfiles.Add(new SearchProfileRecord { Document = json, Version = 1, UpdatedAt = now });
        db.SearchProfileRevisions.Add(new SearchProfileRevision
        {
            Id = Guid.CreateVersion7(now), Version = 1, SavedAt = now, Document = json,
            Summary = "Angelegt aus appsettings.json (und preferences aus profile.json)",
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            _current = new SearchProfileSnapshot(profile, 1, now);
            LogSeeded(logger);
        }
        catch (DbUpdateException)
        {
            // Another API process seeded at the same time — load its row (or fail if it really is something else).
            db.ChangeTracker.Clear();
            var existing = await db.SearchProfiles.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (existing is null)
            {
                throw;
            }

            _current = new SearchProfileSnapshot(Deserialize(existing.Document), existing.Version, existing.UpdatedAt);
        }
    }

    public void Dispose() => _writeLock.Dispose();

    public async Task<SearchProfileSnapshot> SaveAsync(SearchProfile profile, int expectedVersion, string? summaryPrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = SearchProfileRules.Validate(profile);
        if (errors.Count > 0)
        {
            throw new SearchProfileValidationException(errors);
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.SearchProfiles.SingleOrDefaultAsync(r => r.Id == SearchProfileRecord.SingletonId, cancellationToken);
            if (record is null)
            {
                await InitializeAsync(cancellationToken);
                record = await db.SearchProfiles.SingleAsync(r => r.Id == SearchProfileRecord.SingletonId, cancellationToken);
            }

            if (record.Version != expectedVersion)
            {
                throw new SearchProfileConflictException(record.Version);
            }

            var now = timeProvider.GetUtcNow();
            var before = Deserialize(record.Document);
            var json = Serialize(profile);
            record.Document = json;
            record.Version++;
            record.UpdatedAt = now;
            var summary = SearchProfileRules.Summarize(before, profile);
            db.SearchProfileRevisions.Add(new SearchProfileRevision
            {
                Id = Guid.CreateVersion7(now), Version = record.Version, SavedAt = now, Document = json,
                Summary = summaryPrefix is null ? summary : $"{summaryPrefix}{summary}",
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                var latest = await db.SearchProfiles.AsNoTracking().SingleAsync(cancellationToken);
                throw new SearchProfileConflictException(latest.Version);
            }

            await PruneAsync(db, cancellationToken);
            _current = new SearchProfileSnapshot(Deserialize(json), record.Version, now);
            LogSaved(logger, record.Version);
            return _current;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<SearchProfileRevisionInfo>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SearchProfileRevisions.AsNoTracking()
            .OrderByDescending(r => r.Version)
            .Take(limit)
            .Select(r => new SearchProfileRevisionInfo(r.Version, r.SavedAt, r.Summary))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Saves an earlier version as the new current one (history stays complete).</summary>
    public async Task<SearchProfileSnapshot?> RollbackAsync(int version, int expectedVersion, CancellationToken cancellationToken)
    {
        string? document;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            document = await db.SearchProfileRevisions.AsNoTracking()
                .Where(r => r.Version == version)
                .Select(r => r.Document)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return document is null
            ? null
            : await SaveAsync(Deserialize(document), expectedVersion, $"Zurück auf Version {version}: ", cancellationToken);
    }

    public static string Serialize(SearchProfile profile) => JsonSerializer.Serialize(profile, JsonOptions);

    public static SearchProfile Deserialize(string json) =>
        JsonSerializer.Deserialize<SearchProfile>(json, JsonOptions) ?? new SearchProfile();

    private static async Task PruneAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var keepFrom = await db.SearchProfileRevisions
            .OrderByDescending(r => r.Version)
            .Skip(KeptRevisions)
            .Select(r => (int?)r.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (keepFrom is { } oldest)
        {
            await db.SearchProfileRevisions.Where(r => r.Version <= oldest).ExecuteDeleteAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Suchprofil created from the configuration defaults")]
    private static partial void LogSeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Suchprofil saved as version {Version}")]
    private static partial void LogSaved(ILogger logger, int version);
}

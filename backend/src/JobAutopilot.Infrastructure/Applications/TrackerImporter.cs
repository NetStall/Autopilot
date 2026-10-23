using System.Text.Json;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobAutopilot.Infrastructure.Applications;

/// <summary>Configuration section <c>Applications</c>.</summary>
public sealed class ApplicationOptions
{
    public const string SectionName = "Applications";

    /// <summary>The tracker export to import; relative paths are resolved against the API's content root.</summary>
    public string TrackerImportPath { get; set; } = "data/import/bewerbungen-export.json";
}

/// <summary>Outcome of one tracker import.</summary>
public sealed record TrackerImportReport(
    int Total,
    int Imported,
    int Updated,
    int Unchanged,
    int LinkedToVacancy,
    IReadOnlyList<string> Warnings);

/// <summary>Thrown when the export file is missing or not valid JSON (→ 400 with this message).</summary>
public sealed class TrackerImportException(string message) : Exception(message);

/// <summary>
/// Imports the candidate's existing application tracker. Idempotent: rows are matched by their tracker <c>id</c>
/// (<see cref="Application.ExternalRef"/>) — re-running updates instead of duplicating. Imported applications are linked
/// to a stored vacancy when company and title match. Personal data: only counts and ids are logged.
/// </summary>
public sealed partial class TrackerImporter(AppDbContext db, TimeProvider timeProvider, ILogger<TrackerImporter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public async Task<TrackerImportReport> ImportFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new TrackerImportException($"Tracker-Export nicht gefunden: {Path.GetFileName(path)} (erwartet unter data/import/).");
        }

        TrackerExport? export;
        try
        {
            await using var stream = File.OpenRead(path);
            export = await JsonSerializer.DeserializeAsync<TrackerExport>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new TrackerImportException($"Tracker-Export ist kein gültiges JSON (Zeile {(ex.LineNumber ?? 0) + 1}).");
        }

        return await ImportAsync(export?.Applications ?? [], cancellationToken);
    }

    public async Task<TrackerImportReport> ImportAsync(IReadOnlyList<TrackerEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var now = timeProvider.GetUtcNow();
        var warnings = new List<string>();
        int imported = 0, updated = 0, unchanged = 0, linked = 0;

        var refs = entries.Select(e => e.Id?.Trim()).OfType<string>().ToList();
        var existing = await db.Applications.Include(a => a.Events)
            .Where(a => a.ExternalRef != null && refs.Contains(a.ExternalRef))
            .ToDictionaryAsync(a => a.ExternalRef!, StringComparer.Ordinal, cancellationToken);

        foreach (var entry in entries)
        {
            var (mapping, error) = TrackerMapper.Map(entry);
            if (mapping is null)
            {
                warnings.Add(error!);
                continue;
            }

            warnings.AddRange(mapping.Warnings);
            if (existing.TryGetValue(mapping.ExternalRef, out var application))
            {
                var before = Fingerprint(application);
                var trackerIsCurrent = mapping.LastActionAt >= application.LastActionAt;
                Apply(application, mapping);

                // A status changed in the tool after the export was made is not overwritten by the older export.
                if (application.Status != mapping.Status && trackerIsCurrent)
                {
                    application.ChangeStatus(mapping.Status, mapping.LastActionAt, "Aus dem Tracker übernommen");
                }

                if (Fingerprint(application) == before)
                {
                    unchanged++;
                }
                else
                {
                    application.UpdatedAt = now;
                    updated++;
                }
            }
            else
            {
                application = new Application
                {
                    Id = Guid.CreateVersion7(now),
                    Company = mapping.Company,
                    Position = mapping.Position,
                    ExternalRef = mapping.ExternalRef,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                Apply(application, mapping);
                application.Status = mapping.Status;
                application.ChangeStatus(mapping.Status, mapping.AppliedAt, "Aus dem Tracker importiert");
                application.LastActionAt = mapping.LastActionAt;
                db.Applications.Add(application);
                existing[mapping.ExternalRef] = application;
                imported++;
            }

            if (application.VacancyId is null && await FindVacancyAsync(application, cancellationToken) is { } vacancyId)
            {
                application.VacancyId = vacancyId;
                linked++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogImported(logger, entries.Count, imported, updated, linked, warnings.Count);
        return new TrackerImportReport(entries.Count, imported, updated, unchanged, linked, warnings);
    }

    private static void Apply(Application application, TrackerMapping mapping)
    {
        application.Company = mapping.Company;
        application.Position = mapping.Position;
        application.Location = mapping.Location;
        application.Channel = mapping.Channel;
        application.ContactName = mapping.ContactName;
        application.ContactEmail = mapping.ContactEmail;
        application.Url = mapping.Url;
        application.AppliedAt = mapping.AppliedAt;
        if (mapping.LastActionAt > application.LastActionAt)
        {
            application.LastActionAt = mapping.LastActionAt;
        }

        application.SalaryExpectation = mapping.SalaryExpectation;
        application.Documents = [.. mapping.Documents];
        application.NextStep = mapping.NextStep;
        application.Notes = mapping.Notes;
        application.RefreshDuplicateKey();
    }

    /// <summary>The stored vacancy for an application: same duplicate key, or similar company and title.</summary>
    private async Task<Guid?> FindVacancyAsync(Application application, CancellationToken cancellationToken)
    {
        var firstWord = DuplicateKeys.For(application.Company, string.Empty).Split('|')[0].Split(' ')[0];
        if (firstWord.Length < 3)
        {
            return null;
        }

        var pattern = $"%{firstWord.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
        var candidates = await db.Vacancies.AsNoTracking()
            .Where(v => v.DuplicateKey == application.DuplicateKey || EF.Functions.ILike(v.Company, pattern))
            .OrderBy(v => v.FetchedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(v => ApplicationMatcher.Matches(application, v))?.Id;
    }

    private static string Fingerprint(Application a) => string.Join('\u001f',
        a.Company, a.Position, a.Location, a.Channel, a.ContactName, a.ContactEmail, a.Url, a.AppliedAt.ToUnixTimeSeconds(),
        a.LastActionAt.ToUnixTimeSeconds(), a.Status, a.SalaryExpectation, string.Join(',', a.Documents), a.NextStep, a.Notes);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Tracker import: {Total} rows, {Imported} new, {Updated} updated, {Linked} linked to a vacancy, {Warnings} warnings")]
    private static partial void LogImported(ILogger logger, int total, int imported, int updated, int linked, int warnings);
}

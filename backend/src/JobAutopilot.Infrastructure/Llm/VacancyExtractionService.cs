using System.Diagnostics;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>Configuration section <c>Llm:Extraction</c>.</summary>
public sealed class VacancyExtractionOptions
{
    public const string SectionName = "Llm:Extraction";

    /// <summary>Analyse new and changed vacancies after every import (only when the task's provider is usable).</summary>
    public bool AutoAfterImport { get; set; } = true;

    /// <summary>Upper limit of „Top N" for one run („Alle analysieren": 50 / 100 / 200 / 500).</summary>
    public int MaxPerRun { get; set; } = 500;

    /// <summary>After an import only new or changed vacancies within the current Top N are analysed (07b).</summary>
    public int AutoTopN { get; set; } = 100;

    /// <summary>Output cap per answer, so a repetition loop ends early instead of running into the timeout.</summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>Longer descriptions are cut (with „[…]") so prompt and answer fit the model's context.</summary>
    public int MaxDescriptionCharacters { get; set; } = 12000;

    /// <summary>Prompt version (<c>extraction.v2</c>; <c>extraction.v1</c> for comparison). Part of the cache key.</summary>
    public string PromptVersion { get; set; } = ExtractionPrompt.DefaultVersion;

    /// <summary>Let a thinking model reason first. Off: faster and the schema-bound answer is as good for extraction.</summary>
    public bool Thinking { get; set; }
}

public enum ExtractionOutcomeKind
{
    /// <summary>The model was asked and the answer passed validation.</summary>
    Extracted,

    /// <summary>Taken from the cache (same text, model and prompt) — no model call.</summary>
    Cached,

    /// <summary>Invalid twice; the rules stay in charge for this text.</summary>
    Failed,

    /// <summary>No description, or the vacancy is gone.</summary>
    Skipped,
}

public sealed record ExtractionOutcome(ExtractionOutcomeKind Kind, long DurationMs = 0, int OutputTokens = 0);

/// <summary>A vacancy whose extraction failed (invalid twice or timed out) with the current model and prompt.</summary>
public sealed record FailedExtraction(Guid VacancyId, string Title, string Company, int? RankScore, string? Error, DateTimeOffset FailedAt);

/// <summary>
/// Extracts the requirements of one vacancy with the model of the task <c>Extraction</c> (<c>Llm:Tasks</c>), caches the
/// result per description hash, model and prompt version (<see cref="VacancyExtraction"/>), stores it on the vacancy
/// and scores the vacancy again. An invalid answer is sent back once with the validation errors; when the second
/// answer fails too, the failure is cached and the rules stay in charge. A provider that is down throws
/// <see cref="LlmException"/>, so the background run stops instead of failing every vacancy.
/// </summary>
public sealed partial class VacancyExtractionService(
    AppDbContext db,
    ILlmRouter llm,
    ICandidateProfileSource profiles,
    VacancyEnricher enricher,
    Core.SearchProfiles.ISearchProfileSource searchProfiles,
    IOptions<VacancyExtractionOptions> options,
    TimeProvider timeProvider,
    ILogger<VacancyExtractionService> logger)
{
    public bool IsAvailable => llm.IsConfigured(LlmOptions.ExtractionTask);

    public string Model => llm.ModelFor(LlmOptions.ExtractionTask);

    private ExtractionPrompt Prompt => ExtractionPrompt.For(options.Value.PromptVersion);

    /// <summary>
    /// The current Top <paramref name="top"/> by rank among the vacancies I would see — not dismissed („Ausgeblendet"),
    /// not unreachable, not of an employer type the Suchprofil hides — that still need an extraction for their text
    /// with the current model and prompt (07b).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> PendingAsync(int top, CancellationToken cancellationToken)
    {
        var model = Model;
        var version = Prompt.Version;
        var hidden = searchProfiles.Current.Profile.HiddenEmployerTypes();
        var candidates = await db.Vacancies.AsNoTracking()
            .Where(v => v.Description != null && v.Description != string.Empty)
            .Where(v => v.Interest != Core.Vacancies.VacancyInterest.Dismissed && !v.Unreachable && !hidden.Contains(v.EmployerType))
            .WithoutFiltered(db)
            .OrderByDescending(v => v.RankScore ?? v.RelevanceScore ?? 0)
            .ThenByDescending(v => v.FetchedAt)
            .Take(Math.Clamp(top, 1, options.Value.MaxPerRun))
            .Select(v => new { v.Id, v.Description, v.ExtractionKey })
            .ToListAsync(cancellationToken);
        return [.. candidates
            .Where(c => c.ExtractionKey != ExtractionKey.For(ExtractionKey.HashOf(c.Description!), model, version))
            .Select(c => c.Id)];
    }

    /// <summary>„ungültig": vacancies whose extraction failed with the current model and prompt, with the reason.</summary>
    public async Task<IReadOnlyList<FailedExtraction>> FailedAsync(CancellationToken cancellationToken)
    {
        var model = Model;
        var version = Prompt.Version;
        var failed = await db.VacancyExtractions.AsNoTracking()
            .Where(e => e.Status == ExtractionStatus.Failed && e.Model == model && e.PromptVersion == version)
            .ToListAsync(cancellationToken);
        var keys = failed.ToDictionary(e => ExtractionKey.For(e.DescriptionHash, e.Model, e.PromptVersion));
        var vacancies = await db.Vacancies.AsNoTracking()
            .Where(v => v.ExtractionKey != null && keys.Keys.Contains(v.ExtractionKey))
            .Select(v => new { v.Id, v.Title, v.Company, v.RankScore, v.ExtractionKey })
            .ToListAsync(cancellationToken);
        return [.. vacancies
            .Select(v => new FailedExtraction(v.Id, v.Title, v.Company, v.RankScore, keys[v.ExtractionKey!].Error, keys[v.ExtractionKey!].CreatedAt))
            .OrderByDescending(f => f.RankScore ?? 0)];
    }

    /// <summary>„Erneut": forgets a failed attempt for this text, model and prompt, so the next run asks again.</summary>
    public async Task<bool> ForgetFailureAsync(Guid vacancyId, CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.SingleOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null || string.IsNullOrWhiteSpace(vacancy.Description))
        {
            return false;
        }

        var hash = ExtractionKey.HashOf(vacancy.Description);
        var model = Model;
        var version = Prompt.Version;
        await db.VacancyExtractions
            .Where(e => e.Status == ExtractionStatus.Failed && e.DescriptionHash == hash && e.Model == model && e.PromptVersion == version)
            .ExecuteDeleteAsync(cancellationToken);
        if (vacancy.ExtractionJson is null)
        {
            vacancy.ExtractionKey = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<ExtractionOutcome> ExtractAsync(Guid vacancyId, CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.SingleOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null || string.IsNullOrWhiteSpace(vacancy.Description))
        {
            return new ExtractionOutcome(ExtractionOutcomeKind.Skipped);
        }

        var model = Model;
        var version = Prompt.Version;
        var hash = ExtractionKey.HashOf(vacancy.Description);
        var key = ExtractionKey.For(hash, model, version);
        var cached = await db.VacancyExtractions.AsNoTracking()
            .SingleOrDefaultAsync(e => e.DescriptionHash == hash && e.Model == model && e.PromptVersion == version, cancellationToken);
        if (cached is not null)
        {
            Apply(vacancy, cached, hash, key);
            await db.SaveChangesAsync(cancellationToken);
            return new ExtractionOutcome(cached.Status == ExtractionStatus.Succeeded ? ExtractionOutcomeKind.Cached : ExtractionOutcomeKind.Failed);
        }

        var entry = await CallAsync(vacancy, hash, model, cancellationToken);
        db.VacancyExtractions.Add(entry);
        Apply(vacancy, entry, hash, key);
        await db.SaveChangesAsync(cancellationToken);
        LogExtracted(logger, vacancy.Id, model, version, entry.Status, entry.Attempts, entry.OutputTokens, entry.DurationMs);
        return new ExtractionOutcome(
            entry.Status == ExtractionStatus.Succeeded ? ExtractionOutcomeKind.Extracted : ExtractionOutcomeKind.Failed, entry.DurationMs, entry.OutputTokens);
    }

    /// <summary>
    /// The extraction of this vacancy by <paramref name="model"/> (same provider and prompt): from the cache or asked now
    /// and cached — the vacancy itself is not changed. For the evaluation of other models (07c).
    /// </summary>
    public async Task<RequirementExtraction?> ExtractWithAsync(Guid vacancyId, string model, CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null || string.IsNullOrWhiteSpace(vacancy.Description))
        {
            return null;
        }

        var entry = await CachedWithAsync(vacancy.Description, model, cancellationToken);
        if (entry is null)
        {
            entry = await CallAsync(vacancy, ExtractionKey.HashOf(vacancy.Description), model, cancellationToken, model);
            db.VacancyExtractions.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            LogExtracted(logger, vacancy.Id, model, entry.PromptVersion, entry.Status, entry.Attempts, entry.OutputTokens, entry.DurationMs);
        }

        return entry.Status == ExtractionStatus.Succeeded ? StoredExtraction.DeserializeResult(entry.ResultJson) : null;
    }

    /// <summary>The cached extraction of <paramref name="description"/> by <paramref name="model"/> with the current prompt.</summary>
    public Task<VacancyExtraction?> CachedWithAsync(string description, string model, CancellationToken cancellationToken)
    {
        var hash = ExtractionKey.HashOf(description);
        var version = Prompt.Version;
        return db.VacancyExtractions.AsNoTracking()
            .SingleOrDefaultAsync(e => e.DescriptionHash == hash && e.Model == model && e.PromptVersion == version, cancellationToken);
    }

    private async Task<VacancyExtraction> CallAsync(
        Core.Vacancies.Vacancy vacancy, string hash, string model, CancellationToken cancellationToken, string? modelOverride = null)
    {
        var prompt = Prompt;
        var profile = profiles.Current.Usable;
        var knownNames = profile is null ? [.. Core.Fit.TechnologyCatalog.All.Select(t => t.Name)] : ExtractionNormalizer.KnownNames(profile);
        var first = prompt.ExtractMessage(vacancy.Title, vacancy.Company, vacancy.Description!, knownNames, options.Value.MaxDescriptionCharacters);
        var entry = new VacancyExtraction
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            DescriptionHash = hash,
            Model = model,
            PromptVersion = prompt.Version,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        var watch = Stopwatch.StartNew();
        IReadOnlyList<string> errors = [];
        foreach (var message in new[] { first, null })
        {
            var user = message ?? prompt.RetryMessage(first, errors);
            entry.Attempts++;
            string json;
            try
            {
                var response = await llm.CompleteJsonAsync(
                    new LlmRequest(LlmOptions.ExtractionTask, entry.Attempts == 1 ? "extract" : "extract-retry", prompt.Version, prompt.System, user, prompt.JsonSchema,
                        Thinking: options.Value.Thinking, MaxOutputTokens: options.Value.MaxOutputTokens, Model: modelOverride),
                    cancellationToken);
                entry.InputTokens += response.Usage.InputTokens;
                entry.OutputTokens += response.Usage.OutputTokens;
                json = response.Json;
            }
            catch (LlmException ex) when (ex.Error == LlmError.Timeout)
            {
                // One slow posting must not stop the run (07b): this vacancy fails with the reason, the next one follows.
                LogTimedOut(logger, vacancy.Id, vacancy.Description!.Length, entry.OutputTokens);
                errors = [$"Zeitüberschreitung: {ex.Message} (Beschreibung {vacancy.Description.Length} Zeichen)"];
                break;
            }
            catch (LlmException ex) when (ex.Error == LlmError.InvalidResponse)
            {
                // Cut off or empty: worth the one retry like a validation error.
                errors = [ex.Message];
                continue;
            }

            var (result, found) = ExtractionValidator.Parse(json);
            if (result is not null)
            {
                // The text has the last word: one list per item, domains and years only when the posting says so (07a2).
                entry.Status = ExtractionStatus.Succeeded;
                entry.ResultJson = StoredExtraction.SerializeResult(ExtractionGuards.Apply(result, vacancy.Description!));
                entry.DurationMs = watch.ElapsedMilliseconds;
                return entry;
            }

            errors = found;
        }

        entry.Status = ExtractionStatus.Failed;
        var error = string.Join(" ", errors);
        entry.Error = error.Length > VacancyExtraction.ErrorMaxLength ? error[..VacancyExtraction.ErrorMaxLength] : error;
        entry.DurationMs = watch.ElapsedMilliseconds;
        return entry;
    }

    /// <summary>Stores the result on the vacancy (or clears it after a failure) and scores it again.</summary>
    private void Apply(Core.Vacancies.Vacancy vacancy, VacancyExtraction entry, string hash, string key)
    {
        vacancy.ExtractionKey = key;
        vacancy.ExtractionJson = entry.Status == ExtractionStatus.Succeeded && StoredExtraction.DeserializeResult(entry.ResultJson) is { } result
            ? new StoredExtraction(result, hash, entry.Model, entry.PromptVersion, entry.CreatedAt).ToJson()
            : null;
        enricher.Apply(vacancy);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Extraction of vacancy {VacancyId} timed out: description {DescriptionLength} characters, {OutputTokens} output tokens before")]
    private static partial void LogTimedOut(ILogger logger, Guid vacancyId, int descriptionLength, int outputTokens);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Extraction of vacancy {VacancyId}: model {Model}, prompt {PromptVersion}, {Status} after {Attempts} attempt(s), {OutputTokens} output tokens, {DurationMs} ms")]
    private static partial void LogExtracted(ILogger logger, Guid vacancyId, string model, string promptVersion, ExtractionStatus status, int attempts, int outputTokens, long durationMs);
}

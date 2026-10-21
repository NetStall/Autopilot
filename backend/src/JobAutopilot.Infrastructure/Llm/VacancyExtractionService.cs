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

    /// <summary>Vacancies per background run, most relevant first; the rest follows with the next run.</summary>
    public int MaxPerRun { get; set; } = 500;

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
    IOptions<VacancyExtractionOptions> options,
    TimeProvider timeProvider,
    ILogger<VacancyExtractionService> logger)
{
    public bool IsAvailable => llm.IsConfigured(LlmOptions.ExtractionTask);

    public string Model => llm.ModelFor(LlmOptions.ExtractionTask);

    private ExtractionPrompt Prompt => ExtractionPrompt.For(options.Value.PromptVersion);

    /// <summary>Vacancies with a description whose extraction is missing or belongs to another text, model or prompt — most relevant first.</summary>
    public async Task<IReadOnlyList<Guid>> PendingAsync(int max, CancellationToken cancellationToken)
    {
        var model = Model;
        var version = Prompt.Version;
        var candidates = await db.Vacancies.AsNoTracking()
            .Where(v => v.Description != null && v.Description != string.Empty)
            .Select(v => new { v.Id, v.Description, v.ExtractionKey, Rank = v.RankScore ?? v.RelevanceScore ?? 0, v.FetchedAt })
            .ToListAsync(cancellationToken);
        return [.. candidates
            .Where(c => c.ExtractionKey != ExtractionKey.For(ExtractionKey.HashOf(c.Description!), model, version))
            .OrderByDescending(c => c.Rank)
            .ThenByDescending(c => c.FetchedAt)
            .Take(max)
            .Select(c => c.Id)];
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

    private async Task<VacancyExtraction> CallAsync(Core.Vacancies.Vacancy vacancy, string hash, string model, CancellationToken cancellationToken)
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
                    new LlmRequest(LlmOptions.ExtractionTask, entry.Attempts == 1 ? "extract" : "extract-retry", prompt.Version, prompt.System, user, ExtractionPrompt.Schema,
                        Thinking: options.Value.Thinking),
                    cancellationToken);
                entry.InputTokens += response.Usage.InputTokens;
                entry.OutputTokens += response.Usage.OutputTokens;
                json = response.Json;
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Extraction of vacancy {VacancyId}: model {Model}, prompt {PromptVersion}, {Status} after {Attempts} attempt(s), {OutputTokens} output tokens, {DurationMs} ms")]
    private static partial void LogExtracted(ILogger logger, Guid vacancyId, string model, string promptVersion, ExtractionStatus status, int attempts, int outputTokens, long durationMs);
}

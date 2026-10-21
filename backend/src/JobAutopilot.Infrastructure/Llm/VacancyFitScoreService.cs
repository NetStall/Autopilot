using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Profile;
using JobAutopilot.Infrastructure.Claude;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>
/// „KI-Bewertung" (step 07b): the model of the task <c>FitScore</c> judges one vacancy against my profile (score 0–100,
/// verdict, three reasons, blockers). Cached per description, profile, model and prompt (<see cref="VacancyFitScore"/>);
/// runs in the same background queue as the extraction. It is shown next to the rank and does not change it.
/// </summary>
public sealed partial class VacancyFitScoreService(
    AppDbContext db,
    ILlmRouter llm,
    ICandidateProfileSource profiles,
    Core.SearchProfiles.ISearchProfileSource searchProfiles,
    IOptions<VacancyExtractionOptions> options,
    TimeProvider timeProvider,
    ILogger<VacancyFitScoreService> logger)
{
    public const string PromptVersion = "fitscore.v1";

    private static readonly Lazy<(string System, string Score, string Retry)> Prompt = new(Load);

    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "score": { "type": "integer" },
            "verdict": { "type": "string", "enum": ["passt", "eher nicht", "passt nicht"] },
            "reasons": { "type": "array", "items": { "type": "string" } },
            "blockers": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["score", "verdict", "reasons", "blockers"],
          "additionalProperties": false
        }
        """);

    public bool IsAvailable => llm.IsConfigured(LlmOptions.FitScoreTask) && profiles.Current.Usable is not null;

    public string Model => llm.ModelFor(LlmOptions.FitScoreTask);

    /// <summary>The profile text and its hash for the current profile and Suchprofil.</summary>
    public (string Text, string Hash)? CurrentProfile()
    {
        if (profiles.Current.Usable is not { } profile)
        {
            return null;
        }

        var text = FitScoreProfile.Build(profile, searchProfiles.Current.Profile, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        return (text, FitScoreProfile.HashOf(text));
    }

    /// <summary>The key a vacancy's score must have for its current text, profile, model and prompt.</summary>
    public string? ExpectedKey(string? description, string profileHash) =>
        string.IsNullOrWhiteSpace(description) ? null : FitScoreProfile.Key(ExtractionKey.HashOf(description), profileHash, Model, PromptVersion);

    /// <summary>The same Top N as the extraction (visible vacancies by rank) that have no current score.</summary>
    public async Task<IReadOnlyList<Guid>> PendingAsync(int top, CancellationToken cancellationToken)
    {
        if (!IsAvailable || CurrentProfile() is not { } current)
        {
            return [];
        }

        var hidden = searchProfiles.Current.Profile.HiddenEmployerTypes();
        var candidates = await db.Vacancies.AsNoTracking()
            .Where(v => v.Description != null && v.Description != string.Empty)
            .Where(v => v.Interest != Core.Vacancies.VacancyInterest.Dismissed && !v.Unreachable && !hidden.Contains(v.EmployerType))
            .OrderByDescending(v => v.RankScore ?? v.RelevanceScore ?? 0)
            .ThenByDescending(v => v.FetchedAt)
            .Take(Math.Clamp(top, 1, options.Value.MaxPerRun))
            .Select(v => new { v.Id, v.Description, v.AiScoreKey })
            .ToListAsync(cancellationToken);
        return [.. candidates.Where(c => c.AiScoreKey != ExpectedKey(c.Description, current.Hash)).Select(c => c.Id)];
    }

    /// <returns><c>true</c> when a score is stored (new or from the cache).</returns>
    public async Task<bool> ScoreAsync(Guid vacancyId, CancellationToken cancellationToken)
    {
        if (!IsAvailable || CurrentProfile() is not { } current)
        {
            return false;
        }

        var vacancy = await db.Vacancies.SingleOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null || string.IsNullOrWhiteSpace(vacancy.Description))
        {
            return false;
        }

        var model = Model;
        var hash = ExtractionKey.HashOf(vacancy.Description);
        var key = FitScoreProfile.Key(hash, current.Hash, model, PromptVersion);
        var cached = await db.VacancyFitScores.AsNoTracking()
            .SingleOrDefaultAsync(s => s.DescriptionHash == hash && s.ProfileHash == current.Hash && s.Model == model && s.PromptVersion == PromptVersion, cancellationToken);
        var entry = cached ?? await CallAsync(vacancy, current.Text, hash, current.Hash, model, cancellationToken);
        if (cached is null)
        {
            db.VacancyFitScores.Add(entry);
        }

        vacancy.AiScoreKey = key;
        var result = entry.Status == ExtractionStatus.Succeeded ? StoredFitScore.DeserializeResult(entry.ResultJson) : null;
        vacancy.AiScore = result?.Score;
        vacancy.AiScoreJson = result is null ? null : new StoredFitScore(result, key, model, PromptVersion, entry.CreatedAt).ToJson();
        await db.SaveChangesAsync(cancellationToken);
        return result is not null;
    }

    private async Task<VacancyFitScore> CallAsync(
        Core.Vacancies.Vacancy vacancy, string profileText, string hash, string profileHash, string model, CancellationToken cancellationToken)
    {
        var (system, score, retry) = Prompt.Value;
        var description = vacancy.Description!;
        var max = options.Value.MaxDescriptionCharacters;
        var first = new StringBuilder(score)
            .Replace("{profile}", LetterPrompt.Neutralize(profileText))
            .Replace("{title}", LetterPrompt.Neutralize(vacancy.Title))
            .Replace("{company}", LetterPrompt.Neutralize(vacancy.Company))
            .Replace("{location}", LetterPrompt.Neutralize(vacancy.Location ?? "–"))
            .Replace("{description}", LetterPrompt.Neutralize(description.Length > max ? description[..max] + " […]" : description))
            .ToString();
        var entry = new VacancyFitScore
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            DescriptionHash = hash,
            ProfileHash = profileHash,
            Model = model,
            PromptVersion = PromptVersion,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        var watch = Stopwatch.StartNew();
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var user = attempt == 1 ? first : first + "\n\n" + retry.Replace("{errors}", string.Join("\n", errors.Select(e => "- " + e)), StringComparison.Ordinal);
            try
            {
                var response = await llm.CompleteJsonAsync(
                    new LlmRequest(LlmOptions.FitScoreTask, attempt == 1 ? "fitscore" : "fitscore-retry", PromptVersion, system, user, Schema,
                        Thinking: options.Value.Thinking, MaxOutputTokens: 600),
                    cancellationToken);
                entry.InputTokens += response.Usage.InputTokens;
                entry.OutputTokens += response.Usage.OutputTokens;
                var (result, found) = AiFitScoreValidator.Parse(response.Json);
                if (result is not null)
                {
                    entry.Status = ExtractionStatus.Succeeded;
                    entry.ResultJson = StoredFitScore.SerializeResult(result);
                    entry.DurationMs = watch.ElapsedMilliseconds;
                    return entry;
                }

                errors = found;
            }
            catch (LlmException ex) when (ex.Error is LlmError.InvalidResponse or LlmError.Timeout)
            {
                errors = [ex.Message];
                if (ex.Error == LlmError.Timeout)
                {
                    break;
                }
            }
        }

        LogFailed(logger, vacancy.Id, model, string.Join(" ", errors).Length);
        entry.Status = ExtractionStatus.Failed;
        var error = string.Join(" ", errors);
        entry.Error = error.Length > VacancyExtraction.ErrorMaxLength ? error[..VacancyExtraction.ErrorMaxLength] : error;
        entry.DurationMs = watch.ElapsedMilliseconds;
        return entry;
    }

    private static Dictionary<string, JsonElement> ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    private static (string System, string Score, string Retry) Load()
    {
        const string resourceName = "JobAutopilot.Infrastructure.Llm.Prompts.fitscore.v1.md";
        using var stream = typeof(VacancyFitScoreService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{resourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = HtmlComment().Replace(reader.ReadToEnd(), string.Empty);
        var sections = SectionHeading().Split(text);
        var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i + 1 < sections.Length; i += 2)
        {
            bodies[sections[i].Trim()] = sections[i + 1].Trim();
        }

        return (bodies["system"], bodies["score"], bodies["retry"]);
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^## +(\w+) *\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SectionHeading();

    [LoggerMessage(Level = LogLevel.Warning, Message = "KI-Bewertung of vacancy {VacancyId} failed: model {Model}, error length {ErrorLength}")]
    private static partial void LogFailed(ILogger logger, Guid vacancyId, string model, int errorLength);
}

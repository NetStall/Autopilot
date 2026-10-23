using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;

namespace JobAutopilot.Core.Extraction;

/// <summary>
/// The local model's own judgement of one vacancy against my profile (step 07b, „KI-Bewertung"): a score 0–100, a
/// verdict, three reasons and the blockers. Shown next to the rule rank — it does not change the rank (yet).
/// </summary>
public sealed record AiFitScore(int Score, string Verdict, IReadOnlyList<string> Reasons, IReadOnlyList<string> Blockers)
{
    public static readonly string[] Verdicts = ["passt", "eher nicht", "passt nicht"];
}

/// <summary>A score as stored on the vacancy, with the key it was made for (description + profile + model + prompt).</summary>
public sealed record StoredFitScore(AiFitScore Result, string Key, string Model, string PromptVersion, DateTimeOffset ScoredAt)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static string SerializeResult(AiFitScore result) => JsonSerializer.Serialize(result, JsonOptions);

    public static AiFitScore? DeserializeResult(string? json) => Read<AiFitScore>(json);

    /// <summary>The stored score when it still belongs to <paramref name="key"/>; otherwise <c>null</c>.</summary>
    public static StoredFitScore? ForKey(string? json, string? key) =>
        key is not null && Read<StoredFitScore>(json) is { } stored && stored.Key == key ? stored : null;

    public static StoredFitScore? Parse(string? json) => Read<StoredFitScore>(json);

    private static T? Read<T>(string? json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The caps the prompt states, applied in code because qwen3:8b names the blocker but keeps the score (07c: Grenzland
/// „nicht erreichbar: Westerbruch" with 80): not reachable → at most 40, service provider / agency → at most 60. A
/// „nicht erreichbar" blocker on a fully remote posting is dropped — the job board's remote flag wins. The verdict
/// always follows the (capped) score.
/// </summary>
public static class AiFitScoreGuards
{
    public const int UnreachableMax = 40;

    public const int ProviderMax = 60;

    public static AiFitScore Apply(AiFitScore score, bool fullRemote)
    {
        ArgumentNullException.ThrowIfNull(score);
        var blockers = score.Blockers.Where(b => !(fullRemote && IsUnreachable(b))).ToList();
        var cap = blockers.Any(IsUnreachable) ? UnreachableMax : blockers.Any(IsProvider) ? ProviderMax : 100;
        var value = Math.Min(score.Score, cap);
        return score with { Score = value, Verdict = VerdictOf(value), Blockers = blockers };
    }

    public static string VerdictOf(int score) => score >= 70 ? "passt" : score >= 40 ? "eher nicht" : "passt nicht";

    private static bool IsUnreachable(string blocker) => blocker.Contains("nicht erreichbar", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] ProviderWords = ["Dienstleister", "Personalvermittlung", "Zeitarbeit", "Arbeitnehmerüberlassung"];

    private static bool IsProvider(string blocker) => ProviderWords.Any(w => blocker.Contains(w, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Checks the model's answer: score 0–100, one of the verdicts, at most three short reasons.</summary>
public static class AiFitScoreValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static (AiFitScore? Result, IReadOnlyList<string> Errors) Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Raw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Raw>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, [$"The answer is not valid JSON ({ex.Message.Split('.')[0]})."]);
        }

        var errors = new List<string>();
        if (raw?.Score is not (>= 0 and <= 100))
        {
            errors.Add("score must be an integer between 0 and 100.");
        }

        var verdict = raw?.Verdict?.Trim().ToLowerInvariant();
        if (verdict is null || !AiFitScore.Verdicts.Contains(verdict))
        {
            errors.Add("verdict must be one of: passt, eher nicht, passt nicht.");
        }

        static List<string> Clean(List<string>? values, int max) =>
            [.. (values ?? []).Select(v => (v ?? string.Empty).Trim()).Where(v => v.Length > 0).Select(v => v.Length > 200 ? v[..200] : v).Take(max)];

        var reasons = Clean(raw?.Reasons, 3);
        if (reasons.Count == 0)
        {
            errors.Add("reasons must contain up to three short German sentences.");
        }

        return errors.Count > 0 ? (null, errors) : (new AiFitScore(raw!.Score!.Value, verdict!, reasons, Clean(raw.Blockers, 5)), []);
    }

    private sealed class Raw
    {
        public int? Score { get; init; }

        public string? Verdict { get; init; }

        public List<string>? Reasons { get; init; }

        public List<string>? Blockers { get; init; }
    }
}

/// <summary>
/// What the model learns about me for the „KI-Bewertung": summaries, skills by level, gaps in my own phrasing,
/// languages, my city, the Suchprofil's places and remote wish — never name, e-mail or phone. Its hash is part of the
/// cache key, so a changed profile is scored again.
/// </summary>
public static class FitScoreProfile
{
    public static string Build(CandidateProfile profile, SearchProfile? searchProfile, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var text = new StringBuilder();
        foreach (var (key, summary) in profile.Summary.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"Kurzprofil ({key}): {summary}\n");
        }

        var years = ProfileValidator.ExperienceMonths(profile, today) / 12.0;
        text.Append(CultureInfo.InvariantCulture, $"Berufserfahrung: {years:0.#} Jahre\n");
        foreach (var level in new[] { SkillLevel.Production, SkillLevel.Project, SkillLevel.Learning })
        {
            var names = profile.Skills.Where(s => s.Level == level).Select(s => s.Name).ToList();
            if (names.Count > 0)
            {
                var label = level switch { SkillLevel.Production => "beruflich", SkillLevel.Project => "eigene Projekte", _ => "lerne ich gerade" };
                text.Append(CultureInfo.InvariantCulture, $"Skills {label}: {string.Join(", ", names)}\n");
            }
        }

        var none = profile.Skills.Where(s => s.Level == SkillLevel.None).Select(s => s.Name).ToList();
        if (none.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"Keine Erfahrung: {string.Join(", ", none)}\n");
        }

        foreach (var gap in profile.KnownGaps.Where(g => !string.IsNullOrWhiteSpace(g.De)))
        {
            text.Append(CultureInfo.InvariantCulture, $"Lücke {gap.Skill}: {gap.De}\n");
        }

        if (profile.Languages.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"Sprachen: {string.Join(", ", profile.Languages.Select(l => $"{l.NameDe ?? l.Name} {l.Level}"))}\n");
        }

        if (profile.Personal.City is { Length: > 0 } city)
        {
            text.Append(CultureInfo.InvariantCulture, $"Wohnort: {city}\n");
        }

        if (searchProfile is not null)
        {
            var places = searchProfile.Places.Select(p => $"{p.City} ({p.RadiusKm} km)").Concat(searchProfile.AdditionalPlaces).ToList();
            if (places.Count > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"Erreichbare Orte: {string.Join(", ", places)}; außerhalb nur voll remote oder mit viel Homeoffice\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"Remote-Wunsch: {searchProfile.RemotePreference}, deutschlandweit remote: {(searchProfile.RemoteGermany ? "ja" : "nein")}\n");

            // 07c: the model needs the preference to name a service provider or an agency as a blocker.
            var lower = searchProfile.RankedLowerEmployerTypes().Concat(searchProfile.HiddenEmployerTypes()).ToList();
            if (lower.Count > 0)
            {
                text.Append("Arbeitgeber: direkte Arbeitgeber bevorzugt; Personalvermittlung, Zeitarbeit und Dienstleister mit Einsatz beim Kunden passen schlechter\n");
            }
        }

        return text.ToString();
    }

    public static string HashOf(string profileText)
    {
        ArgumentNullException.ThrowIfNull(profileText);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profileText)))[..16];
    }

    public static string Key(string descriptionHash, string profileHash, string model, string promptVersion) =>
        $"{descriptionHash}|{profileHash}|{model}|{promptVersion}";
}

/// <summary>Cache of „KI-Bewertungen": one row per description, profile, model and prompt version.</summary>
public sealed class VacancyFitScore
{
    public Guid Id { get; set; }

    public string DescriptionHash { get; set; } = string.Empty;

    public string ProfileHash { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public ExtractionStatus Status { get; set; }

    public string? ResultJson { get; set; }

    public string? Error { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public long DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

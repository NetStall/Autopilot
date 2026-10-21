using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Extraction;

public enum Seniority
{
    Unknown,
    Junior,
    Mid,
    Senior,
    Lead,
}

public enum EmploymentType
{
    Unknown,
    FullTime,
    PartTime,
    Contract,
    Internship,
    WorkingStudent,
}

/// <summary>
/// What a language model read from one posting (step 07a), validated. <see cref="GermanLevel"/> /
/// <see cref="EnglishLevel"/>: <c>null</c> = not mentioned, <see cref="GermanNotRequired"/> = explicitly not needed.
/// Names in the lists are as the model wrote them; <see cref="ExtractionNormalizer"/> maps them to my skills.
/// </summary>
public sealed record RequirementExtraction(
    IReadOnlyList<string> MustHave,
    IReadOnlyList<string> NiceToHave,
    LanguageLevel? GermanLevel,
    bool GermanNotRequired,
    LanguageLevel? EnglishLevel,
    int? MinYears,
    Seniority Seniority,
    RemoteLevel RemoteLevel,
    int? HomeOfficePercent,
    EmploymentType EmploymentType,
    IReadOnlyList<string> DomainRequirements,
    string Summary,
    int? HomeOfficeDaysPerWeek = null,
    IReadOnlyList<string>? SoftSkills = null);

/// <summary>An extraction as stored on the vacancy: the result plus what it was made from.</summary>
public sealed record StoredExtraction(
    RequirementExtraction Result,
    string DescriptionHash,
    string Model,
    string PromptVersion,
    DateTimeOffset ExtractedAt)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The cache key: same text, model and prompt give the same extraction.</summary>
    public string Key => ExtractionKey.For(DescriptionHash, Model, PromptVersion);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static string SerializeResult(RequirementExtraction result) => JsonSerializer.Serialize(result, JsonOptions);

    public static RequirementExtraction? DeserializeResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RequirementExtraction>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The stored extraction when it still belongs to <paramref name="description"/>; otherwise <c>null</c> (the text
    /// changed). The guards run again on read (they are idempotent), so a fixed guard applies without a new model call (07c).
    /// </summary>
    public static StoredExtraction? ForDescription(string? json, string? description)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredExtraction>(json, JsonOptions);
            return stored is not null && stored.DescriptionHash == ExtractionKey.HashOf(description)
                ? stored with { Result = ExtractionGuards.Apply(stored.Result, description) }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class ExtractionKey
{
    /// <summary>SHA-256 of the description with normalized line ends, hex.</summary>
    public static string HashOf(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var normalized = description.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string For(string descriptionHash, string model, string promptVersion) => $"{descriptionHash}|{model}|{promptVersion}";
}

/// <summary>
/// Parses and checks the model's JSON against the rules of the schema (enum values, ranges, list sizes). The errors are
/// short English sentences that are sent back to the model once („fix these and answer again").
/// </summary>
public static class ExtractionValidator
{
    public const int MaxItems = 30;

    /// <summary>
    /// Small models often copy a whole bullet point; the normaliser takes the known terms out of it, so only really long
    /// entries are sent back (07a2).
    /// </summary>
    public const int MaxItemLength = 200;

    public const int MaxSummaryLength = 600;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static (RequirementExtraction? Result, IReadOnlyList<string> Errors) Parse(string json)
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

        if (raw is null)
        {
            return (null, ["The answer is empty."]);
        }

        var errors = new List<string>();
        var mustHave = List(raw.MustHave, "mustHave", errors);
        var niceToHave = List(raw.NiceToHave, "niceToHave", errors);
        var domains = List(raw.DomainRequirements, "domainRequirements", errors);
        var softSkills = List(raw.SoftSkills, "softSkills", errors);
        var (german, germanNone) = Language(raw.GermanLevel, "germanLevel", errors);
        var (english, _) = Language(raw.EnglishLevel, "englishLevel", errors);
        var seniority = Enum<Seniority>(raw.Seniority, "seniority", errors);
        var remote = raw.RemoteLevel?.Trim().ToLowerInvariant() switch
        {
            null or "" or "unknown" => RemoteLevel.Unknown,
            "onsite" => RemoteLevel.OnSite,
            "hybrid" => RemoteLevel.Hybrid,
            "fullremote" => RemoteLevel.FullRemote,
            _ => Invalid(RemoteLevel.Unknown, "remoteLevel must be one of onsite, hybrid, fullRemote, unknown.", errors),
        };
        var employment = Enum<EmploymentType>(raw.EmploymentType, "employmentType", errors);
        if (raw.MinYears is < 0 or > 30)
        {
            errors.Add("minYears must be between 0 and 30, or null.");
        }

        if (raw.HomeOfficePercent is < 0 or > 100)
        {
            errors.Add("homeOfficePercent must be between 0 and 100, or null.");
        }

        if (raw.HomeOfficeDaysPerWeek is < 0 or > 5)
        {
            errors.Add("homeOfficeDaysPerWeek must be between 0 and 5, or null.");
        }

        var summary = (raw.Summary ?? string.Empty).Trim();
        if (summary.Length == 0)
        {
            errors.Add("summary must be two German sentences.");
        }
        else if (summary.Length > MaxSummaryLength)
        {
            errors.Add($"summary must be at most {MaxSummaryLength} characters.");
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new RequirementExtraction(
            mustHave, niceToHave, german, germanNone, english, raw.MinYears is 0 ? null : raw.MinYears, seniority, remote,
            raw.HomeOfficePercent is 0 ? null : raw.HomeOfficePercent, employment, domains, summary,
            raw.HomeOfficeDaysPerWeek is 0 ? null : raw.HomeOfficeDaysPerWeek, softSkills), []);
    }

    private static List<string> List(List<string>? values, string name, List<string> errors)
    {
        var items = (values ?? []).Select(v => (v ?? string.Empty).Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count > MaxItems)
        {
            errors.Add($"{name} must have at most {MaxItems} entries — keep the most important ones.");
        }

        if (items.Any(i => i.Length > MaxItemLength))
        {
            errors.Add($"{name}: each entry is a short name (at most {MaxItemLength} characters), not a sentence.");
        }

        return items;
    }

    private static (LanguageLevel? Level, bool NotRequired) Language(string? value, string name, List<string> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return (null, false);
        }

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        if (System.Enum.TryParse<LanguageLevel>(text, ignoreCase: true, out var level) && System.Enum.IsDefined(level))
        {
            return (level, false);
        }

        errors.Add($"{name} must be one of A1, A2, B1, B2, C1, C2, Native, none, unknown.");
        return (null, false);
    }

    private static T Enum<T>(string? value, string name, List<string> errors)
        where T : struct, System.Enum
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        if (System.Enum.TryParse<T>(text, ignoreCase: true, out var parsed) && System.Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors.Add($"{name} must be one of {string.Join(", ", System.Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]))}.");
        return default;
    }

    private static T Invalid<T>(T fallback, string error, List<string> errors)
    {
        errors.Add(error);
        return fallback;
    }

    private sealed class Raw
    {
        public List<string>? MustHave { get; init; }

        public List<string>? NiceToHave { get; init; }

        public string? GermanLevel { get; init; }

        public string? EnglishLevel { get; init; }

        public int? MinYears { get; init; }

        public string? Seniority { get; init; }

        public string? RemoteLevel { get; init; }

        public int? HomeOfficePercent { get; init; }

        public string? EmploymentType { get; init; }

        public List<string>? DomainRequirements { get; init; }

        public int? HomeOfficeDaysPerWeek { get; init; }

        public List<string>? SoftSkills { get; init; }

        public string? Summary { get; init; }
    }
}

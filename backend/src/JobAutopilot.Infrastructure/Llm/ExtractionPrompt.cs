using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobAutopilot.Infrastructure.Claude;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>
/// The versioned extraction prompts (<c>Llm/Prompts/extraction.v1.md</c>, <c>extraction.v2.md</c>, embedded; the version
/// comes from <c>Llm:Extraction:PromptVersion</c>) and the JSON schema the answer must follow. The posting is untrusted: it is neutralised (no tags, no control characters), cut to a maximum length and
/// placed between <c>&lt;vacancy&gt;</c> markers.
/// </summary>
public sealed partial class ExtractionPrompt
{
    /// <summary>The default since step 07b; v1 and v2 stay embedded for comparison.</summary>
    public const string DefaultVersion = "extraction.v4";

    public static readonly IReadOnlyList<string> Versions = ["extraction.v1", "extraction.v2", "extraction.v3", "extraction.v4"];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExtractionPrompt> Loaded = new(StringComparer.Ordinal);

    private ExtractionPrompt(string version, string system, string extract, string retry)
    {
        Version = version;
        System = system;
        Extract = extract;
        Retry = retry;
    }

    public static ExtractionPrompt Current => For(DefaultVersion);

    /// <summary>The prompt of one version, e.g. <c>extraction.v1</c>.</summary>
    public static ExtractionPrompt For(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return Versions.Contains(version, StringComparer.Ordinal)
            ? Loaded.GetOrAdd(version, Load)
            : throw new ArgumentException($"Unknown extraction prompt version '{version}'. Known: {string.Join(", ", Versions)}.", nameof(version));
    }

    public string Version { get; }

    /// <summary>The schema of this version: v3 adds home-office days and soft skills.</summary>
    public IReadOnlyDictionary<string, JsonElement> JsonSchema =>
        string.CompareOrdinal(Version, "extraction.v4") >= 0 ? SchemaV4 : string.CompareOrdinal(Version, "extraction.v3") >= 0 ? SchemaV3 : Schema;

    public string System { get; }

    private string Extract { get; }

    private string Retry { get; }

    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "mustHave": { "type": "array", "items": { "type": "string" } },
            "niceToHave": { "type": "array", "items": { "type": "string" } },
            "germanLevel": { "type": "string", "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "Native", "none", "unknown"] },
            "englishLevel": { "type": "string", "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "Native", "none", "unknown"] },
            "minYears": { "type": ["integer", "null"] },
            "seniority": { "type": "string", "enum": ["junior", "mid", "senior", "lead", "unknown"] },
            "remoteLevel": { "type": "string", "enum": ["fullRemote", "hybrid", "onsite", "unknown"] },
            "homeOfficePercent": { "type": ["integer", "null"] },
            "employmentType": { "type": "string", "enum": ["fullTime", "partTime", "contract", "internship", "workingStudent", "unknown"] },
            "domainRequirements": { "type": "array", "items": { "type": "string" } },
            "summary": { "type": "string" }
          },
          "required": ["mustHave", "niceToHave", "germanLevel", "englishLevel", "minYears", "seniority", "remoteLevel",
                       "homeOfficePercent", "employmentType", "domainRequirements", "summary"],
          "additionalProperties": false
        }
        """);

    public static IReadOnlyDictionary<string, JsonElement> SchemaV3 { get; } = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "mustHave": { "type": "array", "items": { "type": "string" } },
            "niceToHave": { "type": "array", "items": { "type": "string" } },
            "softSkills": { "type": "array", "items": { "type": "string" } },
            "germanLevel": { "type": "string", "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "Native", "none", "unknown"] },
            "englishLevel": { "type": "string", "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "Native", "none", "unknown"] },
            "minYears": { "type": ["integer", "null"] },
            "seniority": { "type": "string", "enum": ["junior", "mid", "senior", "lead", "unknown"] },
            "remoteLevel": { "type": "string", "enum": ["fullRemote", "hybrid", "onsite", "unknown"] },
            "homeOfficePercent": { "type": ["integer", "null"] },
            "homeOfficeDaysPerWeek": { "type": ["integer", "null"] },
            "employmentType": { "type": "string", "enum": ["fullTime", "partTime", "contract", "internship", "workingStudent", "unknown"] },
            "domainRequirements": { "type": "array", "items": { "type": "string" } },
            "summary": { "type": "string" }
          },
          "required": ["mustHave", "niceToHave", "softSkills", "germanLevel", "englishLevel", "minYears", "seniority", "remoteLevel",
                       "homeOfficePercent", "homeOfficeDaysPerWeek", "employmentType", "domainRequirements", "summary"],
          "additionalProperties": false
        }
        """);

    /// <summary>v4 (08a): requirement groups <c>{"anyOf": […]}</c> for alternatives, and how to apply.</summary>
    public static IReadOnlyDictionary<string, JsonElement> SchemaV4 { get; } = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "mustHave": { "type": "array", "items": { "type": "object", "properties": { "anyOf": { "type": "array", "items": { "type": "string" }, "minItems": 1 } }, "required": ["anyOf"], "additionalProperties": false } },
            "niceToHave": { "type": "array", "items": { "type": "object", "properties": { "anyOf": { "type": "array", "items": { "type": "string" }, "minItems": 1 } }, "required": ["anyOf"], "additionalProperties": false } },
            "softSkills": { "type": "array", "items": { "type": "string" } },
            "germanLevel": { "type": "string", "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "Native", "none", "unknown"] },
            "englishLevel": { "type": "string", "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "Native", "none", "unknown"] },
            "minYears": { "type": ["integer", "null"] },
            "seniority": { "type": "string", "enum": ["junior", "mid", "senior", "lead", "unknown"] },
            "remoteLevel": { "type": "string", "enum": ["fullRemote", "hybrid", "onsite", "unknown"] },
            "homeOfficePercent": { "type": ["integer", "null"] },
            "homeOfficeDaysPerWeek": { "type": ["integer", "null"] },
            "employmentType": { "type": "string", "enum": ["fullTime", "partTime", "contract", "internship", "workingStudent", "unknown"] },
            "domainRequirements": { "type": "array", "items": { "type": "string" } },
            "summary": { "type": "string" },
            "application": {
              "type": "object",
              "properties": {
                "channel": { "type": "string", "enum": ["email", "portal", "form", "external", "unknown"] },
                "email": { "type": ["string", "null"] },
                "contactName": { "type": ["string", "null"] },
                "salutation": { "type": ["string", "null"] },
                "emailAllowed": { "type": "boolean" },
                "portalOnly": { "type": "boolean" },
                "requested": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["channel", "email", "contactName", "salutation", "emailAllowed", "portalOnly", "requested"],
              "additionalProperties": false
            }
          },
          "required": ["mustHave", "niceToHave", "softSkills", "germanLevel", "englishLevel", "minYears", "seniority", "remoteLevel",
                       "homeOfficePercent", "homeOfficeDaysPerWeek", "employmentType", "domainRequirements", "summary", "application"],
          "additionalProperties": false
        }
        """);

    public string ExtractMessage(string title, string company, string description, IReadOnlyList<string> knownNames, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(knownNames);
        var text = description.Length > maxCharacters ? description[..maxCharacters] + " […]" : description;
        return new StringBuilder(Extract)
            .Replace("{knownNames}", string.Join(", ", knownNames.Select(LetterPrompt.Neutralize)))
            .Replace("{title}", LetterPrompt.Neutralize(title))
            .Replace("{company}", LetterPrompt.Neutralize(company))
            .Replace("{description}", LetterPrompt.Neutralize(text))
            .ToString();
    }

    /// <summary>The first message again plus the validation errors — one retry, then the rules stay in charge.</summary>
    public string RetryMessage(string firstMessage, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return firstMessage + "\n\n" + Retry.Replace("{errors}", string.Join("\n", errors.Select(e => "- " + e)), StringComparison.Ordinal);
    }

    private static Dictionary<string, JsonElement> ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    private static ExtractionPrompt Load(string version)
    {
        var resourceName = $"JobAutopilot.Infrastructure.Llm.Prompts.{version}.md";
        using var stream = typeof(ExtractionPrompt).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{resourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = HtmlComment().Replace(reader.ReadToEnd(), string.Empty);
        var sections = SectionHeading().Split(text);
        var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i + 1 < sections.Length; i += 2)
        {
            bodies[sections[i].Trim()] = sections[i + 1].Trim();
        }

        return new ExtractionPrompt(version, Section(bodies, "system", resourceName), Section(bodies, "extract", resourceName), Section(bodies, "retry", resourceName));
    }

    private static string Section(Dictionary<string, string> bodies, string name, string resourceName) =>
        bodies.TryGetValue(name, out var body) && body.Length > 0
            ? body
            : throw new InvalidOperationException($"Prompt section '## {name}' is missing in {resourceName}.");

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^## +(\w+) *\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SectionHeading();
}

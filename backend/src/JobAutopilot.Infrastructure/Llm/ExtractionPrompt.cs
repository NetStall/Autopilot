using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobAutopilot.Infrastructure.Claude;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>
/// The versioned extraction prompt (<c>Llm/Prompts/extraction.v1.md</c>, embedded) and the JSON schema the answer must
/// follow. The posting is untrusted: it is neutralised (no tags, no control characters), cut to a maximum length and
/// placed between <c>&lt;vacancy&gt;</c> markers.
/// </summary>
public sealed partial class ExtractionPrompt
{
    public const string Version = "extraction.v1";

    private const string ResourceName = "JobAutopilot.Infrastructure.Llm.Prompts.extraction.v1.md";

    private static readonly Lazy<ExtractionPrompt> Loaded = new(Load);

    private ExtractionPrompt(string system, string extract, string retry)
    {
        System = system;
        Extract = extract;
        Retry = retry;
    }

    public static ExtractionPrompt Current => Loaded.Value;

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

    private static ExtractionPrompt Load()
    {
        using var stream = typeof(ExtractionPrompt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = HtmlComment().Replace(reader.ReadToEnd(), string.Empty);
        var sections = SectionHeading().Split(text);
        var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i + 1 < sections.Length; i += 2)
        {
            bodies[sections[i].Trim()] = sections[i + 1].Trim();
        }

        return new ExtractionPrompt(Section(bodies, "system"), Section(bodies, "extract"), Section(bodies, "retry"));
    }

    private static string Section(Dictionary<string, string> bodies, string name) =>
        bodies.TryGetValue(name, out var body) && body.Length > 0
            ? body
            : throw new InvalidOperationException($"Prompt section '## {name}' is missing in {ResourceName}.");

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^## +(\w+) *\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SectionHeading();
}

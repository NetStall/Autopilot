using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Fit;

namespace JobAutopilot.Infrastructure.Claude;

/// <summary>
/// The versioned prompt (<c>Prompts/letter.v1.md</c>, embedded) and the messages built from a <see cref="LetterInput"/>.
/// Facts, fit check and task are separate blocks; the posting goes into <c>&lt;vacancy&gt;</c> with angle brackets
/// replaced, so its text cannot close the block or open another one — it stays data.
/// </summary>
public sealed partial class LetterPrompt
{
    public const string Version = "letter.v1";

    private const string ResourceName = "JobAutopilot.Infrastructure.Claude.Prompts.letter.v1.md";

    private LetterPrompt(string system, string generate, string rephrase)
    {
        System = system;
        Generate = generate;
        Rephrase = rephrase;
    }

    public static LetterPrompt Current { get; } = Load();

    public string System { get; }

    public string Generate { get; }

    public string Rephrase { get; }

    /// <summary>JSON schema of the letter (structured output).</summary>
    public static IReadOnlyDictionary<string, JsonElement> LetterSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "subject": { "type": "string" },
            "salutation": { "type": "string" },
            "paragraphs": { "type": "array", "items": { "type": "string" }, "minItems": 1 },
            "closing": { "type": "string" },
            "language": { "type": "string", "enum": ["de", "en"] },
            "formality": { "type": "string", "enum": ["sie", "du"] },
            "factsUsed": { "type": "array", "items": { "type": "string" } },
            "gapsMentioned": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["subject", "salutation", "paragraphs", "closing", "language", "formality", "factsUsed", "gapsMentioned"],
          "additionalProperties": false
        }
        """);

    /// <summary>JSON schema of one rewritten paragraph.</summary>
    public static IReadOnlyDictionary<string, JsonElement> ParagraphSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "paragraph": { "type": "string" },
            "factsUsed": { "type": "array", "items": { "type": "string" } },
            "gapsMentioned": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["paragraph", "factsUsed", "gapsMentioned"],
          "additionalProperties": false
        }
        """);

    public string GenerateMessage(LetterInput input, int maxVacancyCharacters)
    {
        ArgumentNullException.ThrowIfNull(input);
        var message = new StringBuilder();
        AppendTask(message, input);
        AppendFacts(message, input);
        AppendVacancy(message, input, maxVacancyCharacters);
        message.AppendLine().AppendLine(Generate);
        return message.ToString();
    }

    public string RephraseMessage(LetterInput input, LetterDraft current, int paragraph, string? hint, int maxVacancyCharacters)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);
        var message = new StringBuilder();
        AppendTask(message, input);
        AppendFacts(message, input);
        AppendVacancy(message, input, maxVacancyCharacters);
        message.AppendLine("<current_letter>");
        message.AppendLine(CultureInfo.InvariantCulture, $"subject: {Neutralize(current.Subject)}");
        message.AppendLine(CultureInfo.InvariantCulture, $"salutation: {Neutralize(current.Salutation)}");
        foreach (var (text, i) in current.Paragraphs.Select((p, i) => (p, i)))
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"paragraph {i + 1}: {Neutralize(text)}");
        }

        message.AppendLine(CultureInfo.InvariantCulture, $"closing: {Neutralize(current.Closing)}");
        message.AppendLine("</current_letter>");
        if (!string.IsNullOrWhiteSpace(hint))
        {
            message.AppendLine("<hint>").AppendLine(Neutralize(hint.Trim())).AppendLine("</hint>");
        }

        message.AppendLine().AppendLine(Rephrase.Replace("{{paragraph}}", (paragraph + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        return message.ToString();
    }

    /// <summary>Replaces angle brackets (and control characters) so untrusted text cannot form block tags.</summary>
    public static string Neutralize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var cleaned = ControlCharacters().Replace(text, " ");
        return cleaned.Replace('<', '‹').Replace('>', '›');
    }

    private static void AppendTask(StringBuilder message, LetterInput input)
    {
        var tone = input.Tone;
        message.AppendLine("<task>");
        message.AppendLine(CultureInfo.InvariantCulture, $"language: {(tone.Language == DocumentLanguage.De ? "de (German)" : "en (English)")}");
        message.AppendLine(CultureInfo.InvariantCulture, $"formality: {(tone.Language == DocumentLanguage.En ? "sie (English letter)" : tone.Formality == Formality.Du ? "du (the posting says du)" : "sie")}");
        message.AppendLine(CultureInfo.InvariantCulture, $"salutation: {tone.Salutation}");
        message.AppendLine(CultureInfo.InvariantCulture, $"today: {Formats.Date(input.Today, DocumentLanguage.De)}");
        message.AppendLine(CultureInfo.InvariantCulture, $"job title: {Neutralize(input.Vacancy.Title)}");
        message.AppendLine(CultureInfo.InvariantCulture, $"company: {Neutralize(input.Vacancy.Company)}");
        message.AppendLine("</task>");
    }

    private static void AppendFacts(StringBuilder message, LetterInput input)
    {
        message.AppendLine("<profile_facts>");
        foreach (var fact in input.Facts)
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"- [{fact.Id}] ({fact.Kind}) {Neutralize(fact.Text)}");
        }

        message.AppendLine("</profile_facts>");

        var fit = input.Fit;
        message.AppendLine("<fit_check>");
        message.AppendLine(CultureInfo.InvariantCulture, $"covered (used at work): {List(fit.Requirements.Where(r => r.Coverage == FitCoverage.Covered))}");
        message.AppendLine(CultureInfo.InvariantCulture, $"partial (own projects / learning — not professional experience): {List(fit.Requirements.Where(r => r.Coverage == FitCoverage.Partial))}");
        message.AppendLine(CultureInfo.InvariantCulture, $"gaps, required: {List(fit.Requirements.Where(r => r.Coverage == FitCoverage.Gap && !r.Optional))}");
        message.AppendLine(CultureInfo.InvariantCulture, $"gaps, optional: {List(fit.Requirements.Where(r => r.Coverage == FitCoverage.Gap && r.Optional))}");
        foreach (var warning in fit.Warnings)
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"warning: {Neutralize(warning.Text)}");
        }

        message.AppendLine("</fit_check>");
    }

    private static void AppendVacancy(StringBuilder message, LetterInput input, int maxCharacters)
    {
        var vacancy = input.Vacancy;
        var description = vacancy.Description ?? "(no description — only the title is known)";
        if (description.Length > maxCharacters)
        {
            description = description[..maxCharacters] + " […]";
        }

        message.AppendLine("<vacancy>");
        message.AppendLine(CultureInfo.InvariantCulture, $"title: {Neutralize(vacancy.Title)}");
        message.AppendLine(CultureInfo.InvariantCulture, $"company: {Neutralize(vacancy.Company)}");
        if (vacancy.Location is { } location)
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"location: {Neutralize(location)}");
        }

        message.AppendLine("description:").AppendLine(Neutralize(description));
        message.AppendLine("</vacancy>");
    }

    private static string List(IEnumerable<FitRequirement> requirements)
    {
        var names = requirements.Select(r => Neutralize(r.Name)).ToList();
        return names.Count == 0 ? "—" : string.Join(", ", names);
    }

    private static Dictionary<string, JsonElement> Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    private static LetterPrompt Load()
    {
        using var stream = typeof(LetterPrompt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = HtmlComment().Replace(reader.ReadToEnd(), string.Empty);
        var sections = SectionHeading().Split(text);
        // Split gives [before, name1, body1, name2, body2, …].
        var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i + 1 < sections.Length; i += 2)
        {
            bodies[sections[i].Trim()] = sections[i + 1].Trim();
        }

        return new LetterPrompt(Section(bodies, "system"), Section(bodies, "generate"), Section(bodies, "rephrase"));
    }

    private static string Section(Dictionary<string, string> bodies, string name) =>
        bodies.TryGetValue(name, out var body) && body.Length > 0
            ? body
            : throw new InvalidOperationException($"Prompt section '## {name}' is missing in {ResourceName}.");

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^## +(\w+) *\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ControlCharacters();
}

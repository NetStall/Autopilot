using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Documents;

/// <summary>A named contact person, e.g. „Frau Müller" → <c>(Female, "Anna", "Müller")</c>.</summary>
public sealed record ContactPerson(ContactGender Gender, string? FirstName, string LastName)
{
    /// <summary>„Frau Anna Müller".</summary>
    public string Display => $"{(Gender == ContactGender.Female ? "Frau" : "Herr")} {(FirstName is null ? "" : FirstName + " ")}{LastName}";

    /// <summary>The salutation line for this person in the given tone.</summary>
    public string Salutation(DocumentLanguage language, Formality formality) => language switch
    {
        DocumentLanguage.En => $"Dear {(Gender == ContactGender.Female ? "Ms" : "Mr")} {LastName},",
        _ when formality == Formality.Du => $"Hallo {FirstName ?? LastName},",
        _ => Gender == ContactGender.Female ? $"Sehr geehrte Frau {LastName}," : $"Sehr geehrter Herr {LastName},",
    };
}

public enum ContactGender
{
    Female,
    Male,
}

/// <summary>Language, „du"/„Sie" and contact person of a posting.</summary>
public sealed record ToneResult(DocumentLanguage Language, Formality Formality, ContactPerson? Contact)
{
    /// <summary>Salutation without a named person.</summary>
    public string DefaultSalutation => Language switch
    {
        DocumentLanguage.En => "Dear Hiring Team,",
        _ when Formality == Formality.Du => "Hallo liebes Team,",
        _ => "Sehr geehrte Damen und Herren,",
    };

    public string Salutation => Contact?.Salutation(Language, Formality) ?? DefaultSalutation;

    /// <summary>
    /// The salutation with the company when no person is named (letter.v2, 06c): „Hallo liebes Brückenwerk-Team,",
    /// „Sehr geehrtes Brückenwerk-Team,", „Dear Brückenwerk team," — never a bare „Hallo liebes Team,".
    /// </summary>
    public string SalutationFor(string? company)
    {
        if (Contact is not null || CompanyNames.ShortName(company) is not { } name)
        {
            return Salutation;
        }

        return Language switch
        {
            DocumentLanguage.En => $"Dear {name} team,",
            _ when Formality == Formality.Du => $"Hallo liebes {name}-Team,",
            _ => $"Sehr geehrtes {name}-Team,",
        };
    }
}

/// <summary>The short name of a company for a salutation: „Brückenwerk IT Services GmbH" → „Brückenwerk", „k-werk Gruppe GmbH" → „k-werk".</summary>
public static partial class CompanyNames
{
    public static string? ShortName(string? company)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            return null;
        }

        var words = LegalForm().Replace(company, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return null;
        }

        // A short name stays whole („Lindenhof", „Vektor"); a long one becomes its first word, unless that is generic.
        var name = words.Length <= 2 && !GenericWord().IsMatch(words[^1]) ? string.Join(' ', words) : words[0];
        return name.Length >= 2 && !GenericWord().IsMatch(name) ? name : null;
    }

    [GeneratedRegex(@"\b(?:GmbH(?:\s*&\s*Co\.?\s*KG)?|mbH|AG|SE|KG|KGaA|OHG|UG(?:\s*\(haftungsbeschränkt\))?|e\.\s?V\.|Inc\.?|Ltd\.?|LLC|B\.V\.|S\.A\.|Deutschland|Germany|Group|Gruppe|GROUP|Holding|IT[- ]Services|Services|Solutions|Business|Consulting)\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex LegalForm();

    [GeneratedRegex(@"^(?:Ing-Büro|Ingenieurbüro|Büro|Firma|Software|Systems|Stadt|Landkreis|Universität)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GenericWord();
}

/// <summary>
/// Detects the language of a posting (German / English) and whether it addresses applicants with „du" or „Sie" — from
/// the text, e.g. „Wir freuen uns auf dich" → du. Rule-based and deterministic; when in doubt German and „Sie".
/// </summary>
public static partial class ToneDetector
{
    private static readonly HashSet<string> GermanWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "der", "die", "das", "und", "wir", "mit", "für", "ist", "sind", "eine", "einen", "unser", "unsere", "bei", "auf",
        "oder", "deine", "ihre", "werden", "suchen", "zur", "zum", "von", "im", "als", "kenntnisse", "erfahrung",
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "we", "with", "for", "is", "are", "our", "you", "your", "will", "of", "to", "in", "as", "experience",
        "team", "looking", "skills", "role", "work",
    };

    public static ToneResult Detect(string title, string? description)
    {
        var text = $"{title}\n{description}";
        var language = DetectLanguage(text);
        var formality = language == DocumentLanguage.De ? DetectFormality(text) : Formality.Sie;
        return new ToneResult(language, formality, DetectContact(text));
    }

    public static DocumentLanguage DetectLanguage(string text)
    {
        var german = 0;
        var english = 0;
        foreach (Match word in WordPattern().Matches(text))
        {
            if (GermanWords.Contains(word.Value))
            {
                german++;
            }
            else if (EnglishWords.Contains(word.Value))
            {
                english++;
            }
        }

        // Umlauts and „(m/w/d)" are German even in a short, otherwise English title („Senior Developer (m/w/d)").
        if (UmlautPattern().IsMatch(text))
        {
            german += 2;
        }

        return english > german * 1.5 && english >= 3 ? DocumentLanguage.En : DocumentLanguage.De;
    }

    /// <summary>
    /// Counts „du" forms (du, dich, dir, dein…, euch) against polite forms (Ihnen, Ihr…, Sie in mid-sentence).
    /// „Sie" at the start of a sentence can mean „they" and is not counted.
    /// </summary>
    public static Formality DetectFormality(string text)
    {
        var du = DuPattern().Count(text) + (DuPhrasePattern().IsMatch(text) ? 2 : 0);
        var sie = SieFormPattern().Count(text) + MidSentenceSiePattern().Count(text);
        return du > sie ? Formality.Du : Formality.Sie;
    }

    /// <summary>„z. Hd. Frau Müller", „Ansprechpartnerin: Frau Anna Müller", „Ihr Kontakt: Herr Max Mustermann".</summary>
    public static ContactPerson? DetectContact(string text)
    {
        var match = ContactPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var gender = match.Groups["title"].Value.StartsWith("Frau", StringComparison.Ordinal) ? ContactGender.Female : ContactGender.Male;
        var first = match.Groups["first"].Success && match.Groups["last"].Success ? match.Groups["first"].Value : null;
        var last = match.Groups["last"].Success ? match.Groups["last"].Value : match.Groups["first"].Value;
        return new ContactPerson(gender, first, last);
    }

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant, 250)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"[äöüÄÖÜß]|\(m/w/d\)|\(w/m/d\)|\(m/f/d\)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex UmlautPattern();

    [GeneratedRegex(@"\b(du|dich|dir|dein|deine|deinen|deinem|deiner|deines|euch|euer|eure|Du|Dich|Dir|Dein|Deine|Deinen|Deinem|Deiner|Deines)\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex DuPattern();

    [GeneratedRegex(@"freuen uns auf (dich|deine|Dich|Deine)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex DuPhrasePattern();

    [GeneratedRegex(@"\b(Ihnen|Ihr|Ihre|Ihren|Ihrem|Ihrer|Ihres)\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SieFormPattern();

    /// <summary>„… freuen uns, Sie kennenzulernen" — „Sie" not at the start of a sentence or line.</summary>
    [GeneratedRegex(@"(?<![.!?:\n]\s*)(?<=\p{L}[,]?\s+)Sie\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex MidSentenceSiePattern();

    [GeneratedRegex(
        @"(?:z\.\s?Hd\.?|Ansprechpartner(?:in)?|Ansprechperson|Kontakt(?:person)?|Ihr(?:e)?\s+Ansprechpartner(?:in)?|Dein(?:e)?\s+Ansprechpartner(?:in)?)\s*(?:ist|für\s+\p{L}+(?:\s+\p{L}+)?)?\s*:?\s*(?<title>Frau|Herrn?)\s+(?:(?:Dr|Prof)\.\s*)*(?<first>\p{Lu}[\p{Ll}\-]+)(?:\s+(?<last>\p{Lu}[\p{L}\-]+))?",
        RegexOptions.CultureInvariant,
        250)]
    private static partial Regex ContactPattern();
}

using JobAutopilot.Core.Applications;
using JobAutopilot.Core.SearchProfiles;

namespace JobAutopilot.Core.Documents;

/// <summary>
/// The channel texts of an application package (08c), generated in one call after the letter: a short e-mail body
/// (3–5 sentences, the letter's tone), a short motivation for forms (≤ 1,000 characters) and „Über mich" (≤ 500). The
/// fact guard's warnings on them are kept here. Nothing is ever sent — I copy, attach and send myself.
/// </summary>
public sealed record PackageTexts(
    string EmailBody,
    string Motivation,
    string AboutMe,
    IReadOnlyList<DraftWarning> Warnings,
    DateTimeOffset GeneratedAt)
{
    public const int MaxEmailBody = 1200;
    public const int MaxMotivation = 1000;
    public const int MaxAboutMe = 500;
}

/// <summary>The generated channel texts before the fact guard ran.</summary>
public sealed record PackageGeneration(string EmailBody, string Motivation, string AboutMe, TokenUsage Usage, string Model, string PromptVersion);

/// <summary>Drafts the package texts from the same facts as the letter (Claude in Infrastructure; faked in tests).</summary>
public interface IPackageTextGenerator
{
    Task<PackageGeneration> GenerateAsync(LetterInput input, LetterDraft letter, CancellationToken cancellationToken);
}

/// <summary>One line of the portal / form checklist.</summary>
public sealed record ChecklistItem(string Label, string? Value);

/// <summary>Builds the deterministic part of a package: the checklist for portals and forms.</summary>
public static class ApplicationPackage
{
    /// <summary>
    /// What a portal or form usually asks, filled from the Suchprofil where I set it: salary, start („ab sofort"
    /// without a date), a note on Arbeitszeugnisse when they are asked for, the documents themselves.
    /// </summary>
    public static IReadOnlyList<ChecklistItem> Checklist(HowToApply how, SearchProfile profile, DocumentLanguage language)
    {
        ArgumentNullException.ThrowIfNull(how);
        ArgumentNullException.ThrowIfNull(profile);
        var items = new List<ChecklistItem>
        {
            new("Lebenslauf (PDF)", null),
        };
        if (!how.Requested.Contains("ohne Anschreiben", StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new("Anschreiben (PDF)", null));
        }

        if (how.Requested.Contains("Gehaltsvorstellung", StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new("Gehaltsvorstellung", Formats.SalaryRange(profile.SalaryMin, profile.SalaryMax, language) ?? "im Suchprofil nicht hinterlegt"));
        }

        var start = profile.EarliestStart is { Length: > 0 } earliest ? Formats.Date(earliest, language) : language == DocumentLanguage.En ? "immediately" : "ab sofort";
        items.Add(new("Frühester Eintritt", start));
        if (how.Requested.Contains("Zeugnisse", StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new("Hinweis zu Arbeitszeugnissen", "Arbeitszeugnisse reiche ich gern auf Anfrage nach."));
        }

        return items;
    }

}

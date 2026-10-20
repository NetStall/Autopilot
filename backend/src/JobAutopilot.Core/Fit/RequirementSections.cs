using System.Text.RegularExpressions;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Fit;

/// <summary>How important a requirement is to the employer — decides the penalty when it is a gap.</summary>
public enum RequirementImportance
{
    /// <summary>Offered as an alternative to a skill I cover („Angular oder Vue.js") — no penalty.</summary>
    Alternative,

    /// <summary>„Ein Plus", „von Vorteil", „wünschenswert" or a nice-to-have section.</summary>
    NiceToHave,

    /// <summary>Mentioned without a section (title, tasks).</summary>
    Required,

    /// <summary>In a must-have section („Was du mitbringst", „Anforderungen") or marked „(Must-have)".</summary>
    MustHave,
}

public enum SectionKind
{
    None,
    MustHave,
    NiceToHave,
    Other,
}

/// <summary>
/// Splits a posting into sections by its headings: a line that names a must-have heading („Was du mitbringst",
/// „Dein Profil", „Anforderungen", „Requirements" …), a nice-to-have heading („Ein Plus, aber kein Muss",
/// „Wünschenswert", „Nice to have" …) or another section („Deine Aufgaben", „Wir bieten", „Benefits" …) starts that
/// section until the next known heading. Sub-headings that are not in the lists („### Backend") do not end a section.
/// A heading is a short line (no bullet) — a sentence like „Kenntnisse in Tailwind sind von Vorteil" is not.
/// </summary>
public sealed partial class RequirementSections
{
    public const int MaxHeadingLength = 70;

    private readonly List<(int Start, SectionKind Kind)> _starts = [];

    private RequirementSections()
    {
    }

    public static RequirementSections Parse(string text, TermMatcher mustHave, TermMatcher niceToHave, TermMatcher other)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sections = new RequirementSections();
        var position = 0;
        foreach (var line in text.Split('\n'))
        {
            var heading = HeadingText(line);
            if (heading is not null)
            {
                // Nice-to-have first: „Ein Plus, aber kein Muss" must not count as a must-have heading.
                var kind = niceToHave.FindFirst(heading) is not null ? SectionKind.NiceToHave
                    : mustHave.FindFirst(heading) is not null ? SectionKind.MustHave
                    : other.FindFirst(heading) is not null ? SectionKind.Other
                    : SectionKind.None;
                if (kind != SectionKind.None)
                {
                    sections._starts.Add((position, kind));
                }
            }

            position += line.Length + 1;
        }

        return sections;
    }

    /// <summary>The section a text position belongs to.</summary>
    public SectionKind At(int index)
    {
        var kind = SectionKind.None;
        foreach (var (start, sectionKind) in _starts)
        {
            if (start > index)
            {
                break;
            }

            kind = sectionKind;
        }

        return kind;
    }

    /// <summary>The line's text without Markdown and trailing colon when it looks like a heading; otherwise <c>null</c>.</summary>
    public static string? HeadingText(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || BulletStart().IsMatch(trimmed))
        {
            return null;
        }

        var text = Markup().Replace(trimmed, string.Empty).Trim().TrimEnd(':', '!').Trim();
        return text.Length is > 2 and <= MaxHeadingLength && !text.EndsWith('.') ? text : null;
    }

    [GeneratedRegex(@"^(?:[-•·▪–*]\s|\d+[.)]\s)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex BulletStart();

    [GeneratedRegex(@"^#+\s*|\*+|_+|`", RegexOptions.CultureInvariant, 250)]
    private static partial Regex Markup();
}

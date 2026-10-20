using System.Globalization;
using System.Text;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Documents;

/// <summary>
/// Which of the profile's skills a posting mentions, and how well a tagged profile item (bullet, project) fits it —
/// used to pick and order facts for the letter and the CV. Whole-word matching like the scoring (<see cref="TermMatcher"/>).
/// </summary>
public sealed class VacancyTerms
{
    private readonly HashSet<string> _present;
    private readonly string _text;

    public VacancyTerms(CandidateProfile profile, string title, string? description)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _text = $"{title}\n{description}";
        _present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in profile.Skills)
        {
            if (new TermMatcher(skill.AllNames()).FindFirst(_text) is not null)
            {
                foreach (var name in skill.AllNames())
                {
                    _present.Add(Normalize(name));
                }

                if (skill.Group is { Length: > 0 } group)
                {
                    _present.Add(Normalize(group));
                }
            }
        }

        TitleWeighted = new TermMatcher(profile.Skills.Where(s => s.Level is SkillLevel.Production or SkillLevel.Project).SelectMany(s => s.AllNames()));
        Title = title;
    }

    public string Title { get; }

    private TermMatcher TitleWeighted { get; }

    /// <summary>Normalized names of the profile skills that occur in the posting (incl. their aliases and group).</summary>
    public IReadOnlySet<string> PresentSkills => _present;

    public bool Mentions(Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill.AllNames().Any(n => _present.Contains(Normalize(n)));
    }

    /// <summary>
    /// Tags that name a skill found in the posting count 2, tags found literally in the posting 1, profile skills in
    /// the item's text found in the posting 1 each.
    /// </summary>
    public int Score(IEnumerable<string> tags, string text)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var score = 0;
        foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (_present.Contains(Normalize(tag)))
            {
                score += 2;
            }
            else if (tag.Length > 2 && new TermMatcher([tag]).FindFirst(_text) is not null)
            {
                score++;
            }
        }

        score += TitleWeighted.FindAll(text).Select(f => Normalize(f.Term)).Distinct(StringComparer.Ordinal).Count(_present.Contains);
        return score;
    }

    /// <summary>„C#" → <c>csharp</c>, „.NET" → <c>dotnet</c>, „Vue.js" → <c>vuejs</c>: lowercase letters and digits only.</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim().ToLowerInvariant()
            .Replace("c#", "csharp", StringComparison.Ordinal)
            .Replace("c++", "cplusplus", StringComparison.Ordinal)
            .Replace(".net", "dotnet", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

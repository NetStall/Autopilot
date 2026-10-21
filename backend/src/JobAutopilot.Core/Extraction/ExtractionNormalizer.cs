using System.Text.RegularExpressions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Extraction;

/// <summary>
/// One extracted requirement after normalisation: my skill, a catalog technology or the model's own name.
/// <see cref="Method"/>: a named method (DDD, CQRS, Event Sourcing, TDD …) — its gaps share a small cap.
/// </summary>
public sealed record ResolvedRequirement(string Name, Skill? Skill, RequirementImportance Importance, bool Domain, bool Method = false);

/// <summary>
/// Maps the model's names to my profile skills (name and specific aliases, <see cref="SkillTerms.Evidence"/>) and to the
/// technology catalog. Concepts the model may phrase freely — testing („automatisierte Qualitätssicherung", „TDD",
/// „Unit Tests") and CI/CD — map to the profile skill that covers them, generic aliases included. Generic practices
/// („Cloud- und DevOps-Praktiken", „Qualitätsbewusstsein") are dropped. An entry like „Angular oder Vue.js" that names a
/// skill I cover makes the others in it an alternative (no penalty); a part of a counted gap (ECS next to AWS) is none.
/// </summary>
public static partial class ExtractionNormalizer
{
    private static readonly string[] TestingNames = ["Unit Tests", "Unit-Tests", "unit testing", "Testing", "Tests", "Testautomatisierung"];
    private static readonly string[] TestingSkills = ["xUnit", "NUnit", "MSTest", "Vitest", "Jest", "Playwright", "Cypress", "Selenium"];
    private static readonly string[] CiNames = ["CI/CD", "CI", "Continuous Integration", "Continuous Delivery"];
    private static readonly string[] CiSkills = ["GitHub Actions", "GitLab CI", "Jenkins", "TeamCity", "Azure DevOps"];

    /// <summary>Names the prompt offers the model so it writes „C#" rather than „C-Sharp-Kenntnisse".</summary>
    public static IReadOnlyList<string> KnownNames(CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return [.. profile.Skills.Select(s => s.Name).Concat(TechnologyCatalog.All.Select(t => t.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>DDD, CQRS, Event Sourcing, TDD, BDD, Clean/Hexagonal Architecture.</summary>
    public static bool IsMethod(string name) => MethodPattern().IsMatch(name);

    public static IReadOnlyList<ResolvedRequirement> Resolve(RequirementExtraction extraction, CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(profile);
        var skills = profile.Skills.Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => (Skill: s, Matcher: new TermMatcher(SkillTerms.Evidence(s)))).ToList();
        var inProfile = new HashSet<string>(profile.Skills.SelectMany(s => s.AllNames()).Select(SkillTerms.Key), StringComparer.Ordinal);
        var testing = ConceptSkill(profile, TestingNames, TestingSkills);
        var ci = ConceptSkill(profile, CiNames, CiSkills);
        var result = new Dictionary<string, ResolvedRequirement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entries, importance, domain) in new[]
                 {
                     (extraction.DomainRequirements, RequirementImportance.MustHave, true),
                     (extraction.MustHave, RequirementImportance.MustHave, false),
                     (extraction.NiceToHave, RequirementImportance.NiceToHave, false),
                 })
        {
            foreach (var entry in entries)
            {
                var found = new List<(string Name, Skill? Skill)>();
                found.AddRange(skills.Where(s => s.Matcher.FindFirst(entry) is not null).Select(s => (s.Skill.Name, (Skill?)s.Skill)));
                if (found.Count == 0 && !domain && TestingConcept().IsMatch(entry) && testing is not null)
                {
                    found.Add((testing.Name, testing));
                }

                if (found.Count == 0 && !domain && CiConcept().IsMatch(entry) && ci is not null)
                {
                    found.Add((ci.Name, ci));
                }

                found.AddRange(TechnologyCatalog.Matchers
                    .Where(t => t.Matcher.FindFirst(entry) is not null && !t.Technology.AllNames().Any(n => inProfile.Contains(SkillTerms.Key(n))))
                    .Select(t => (t.Technology.Name, (Skill?)null)));
                // A short unknown name stays visible as a gap; a copied sentence without a known term says nothing checkable.
                if (found.Count == 0 && IsShortName(entry) && !SkillTerms.IsGeneric(entry) && !GenericPractice().IsMatch(entry))
                {
                    found.Add((entry.Trim(), null));
                }

                // „Angular oder Vue.js": with one of them covered, the others cost nothing.
                var coveredHere = found.Any(f => f.Skill?.Level == SkillLevel.Production);
                foreach (var (name, skill) in found)
                {
                    var own = coveredHere && skill?.Level != SkillLevel.Production && found.Count > 1 ? RequirementImportance.Alternative : importance;
                    if (!result.TryGetValue(name, out var existing) || own > existing.Importance)
                    {
                        result[name] = new ResolvedRequirement(name, skill, own, domain || existing?.Domain == true, skill is null && IsMethod(name));
                    }
                }
            }
        }

        // A part of a technology that is already a requirement (Amazon ECS next to AWS) is no second gap.
        foreach (var technology in TechnologyCatalog.All.Where(t => t.Parent is not null && result.ContainsKey(t.Name) && result.ContainsKey(t.Parent!)))
        {
            if (result[technology.Name].Skill is null)
            {
                result.Remove(technology.Name);
            }
        }

        return [.. result.Values];
    }

    private static bool IsShortName(string entry) =>
        entry.Trim().Length <= 40 && entry.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;

    /// <summary>The profile skill that covers a concept: one listing it as a name or alias, else a known tool for it — the highest level wins.</summary>
    private static Skill? ConceptSkill(CandidateProfile profile, string[] names, string[] tools) =>
        profile.Skills
            .Where(s => s.Level is SkillLevel.Production or SkillLevel.Project or SkillLevel.Learning)
            .Where(s => s.AllNames().Any(n => names.Contains(n.Trim(), StringComparer.OrdinalIgnoreCase)) || tools.Contains(s.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Level)
            .FirstOrDefault();

    [GeneratedRegex(@"\b(?:DDD|Domain[- ]Driven[- ]Design|CQRS|Event[- ]Sourcing|TDD|Test[- ]Driven[- ]Development|BDD|Clean Architecture|Hexagonal\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex MethodPattern();

    [GeneratedRegex(@"\b(?:unit[- ]?tests?|unit[- ]testing|TDD|test[- ]driven\w*|automatisierte\s+(?:tests?|qualitätssicherung)|testautomatisierung|qualitätssicherung|integrationstests?|automated\s+tests?|testing)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex TestingConcept();

    [GeneratedRegex(@"\b(?:CI/CD|CI|CD|continuous\s+(?:integration|delivery|deployment))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex CiConcept();

    [GeneratedRegex(@"(?:praktik|kultur|bewusstsein|denken|mindset|verständnis|best practices|moderne softwareentwicklung|sauberer code|teamfähig|kommunikation|engineering culture)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GenericPractice();
}

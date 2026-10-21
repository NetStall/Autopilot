using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Extraction;

/// <summary>One extracted requirement after normalisation: my skill, a catalog technology or the model's own name.</summary>
public sealed record ResolvedRequirement(string Name, Skill? Skill, RequirementImportance Importance, bool Domain);

/// <summary>
/// Maps the model's names to my profile skills (name and specific aliases, <see cref="SkillTerms.Evidence"/>) and to the
/// technology catalog. An entry like „Angular oder Vue.js" that names a skill I cover makes the others in it an
/// alternative (no penalty). Generic words („Teamfähigkeit" is not asked for, „CI/CD" is no evidence) are dropped.
/// </summary>
public static class ExtractionNormalizer
{
    /// <summary>Names the prompt offers the model so it writes „C#" rather than „C-Sharp-Kenntnisse".</summary>
    public static IReadOnlyList<string> KnownNames(CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return [.. profile.Skills.Select(s => s.Name).Concat(TechnologyCatalog.All.Select(t => t.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<ResolvedRequirement> Resolve(RequirementExtraction extraction, CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(profile);
        var skills = profile.Skills.Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => (Skill: s, Matcher: new TermMatcher(SkillTerms.Evidence(s)))).ToList();
        var inProfile = new HashSet<string>(profile.Skills.SelectMany(s => s.AllNames()).Select(SkillTerms.Key), StringComparer.Ordinal);
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
                found.AddRange(TechnologyCatalog.Matchers
                    .Where(t => t.Matcher.FindFirst(entry) is not null && !t.Technology.AllNames().Any(n => inProfile.Contains(SkillTerms.Key(n))))
                    .Select(t => (t.Technology.Name, (Skill?)null)));
                if (found.Count == 0 && !SkillTerms.IsGeneric(entry))
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
                        result[name] = new ResolvedRequirement(name, skill, own, domain || existing?.Domain == true);
                    }
                }
            }
        }

        return [.. result.Values];
    }
}

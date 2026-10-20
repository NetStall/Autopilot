namespace JobAutopilot.Core.Profile;

/// <summary>
/// Which names of a profile skill count as evidence that a posting asks for exactly that skill. The skill's name and
/// its specific aliases do; generic concepts do not, even when listed as an alias — „CI/CD" in a posting is no
/// evidence for GitHub Actions, „Unit Tests" none for Vitest, „Container" none for Docker.
/// </summary>
public static class SkillTerms
{
    private static readonly HashSet<string> GenericKeys = new(StringComparer.Ordinal)
    {
        "ci", "cd", "cicd", "ci/cd", "continuousintegration", "continuousdelivery", "continuousdeployment",
        "test", "tests", "testing", "unittest", "unittests", "unittesting", "integrationtests", "testautomation",
        "testautomatisierung", "automatisiertetests", "container", "containers", "containerisierung", "cloud",
        "devops", "pipeline", "pipelines", "monitoring", "logging", "agile", "agil", "scrum", "api", "apis",
        "frontend", "backend", "fullstack", "database", "databases", "datenbank", "datenbanken", "web",
        "webentwicklung", "softwareentwicklung", "programming", "programmierung", "versioncontrol", "versionskontrolle",
    };

    /// <summary>„Unit-Tests" → <c>unittests</c>: lowercase, without spaces, hyphens and dots.</summary>
    public static string Key(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return string.Concat(term.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '.' and not '_'));
    }

    public static bool IsGeneric(string term) => GenericKeys.Contains(Key(term));

    /// <summary>The skill's name and its non-generic aliases.</summary>
    public static IEnumerable<string> Evidence(Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill.AllNames().Where((name, index) => index == 0 || !IsGeneric(name));
    }

    /// <summary>Aliases that are ignored as evidence (for the profile's warnings).</summary>
    public static IEnumerable<string> IgnoredAliases(Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill.Aliases.Where(a => !string.IsNullOrWhiteSpace(a) && IsGeneric(a));
    }
}

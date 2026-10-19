using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Applications;

/// <summary>
/// Decides whether a vacancy is one I already applied to: linked directly, the same duplicate key, or a similar
/// company (one name is the start of the other: „TGW" / „TGW Logistics GmbH") with a similar title (most words in
/// common). Used for the „Beworben" badge — so I never apply twice by accident — and to link imported applications.
/// </summary>
public static class ApplicationMatcher
{
    private const double TitleSimilarity = 0.6;

    private static readonly HashSet<string> TitleNoise = new(StringComparer.Ordinal)
    {
        "m", "w", "d", "f", "x", "div", "all", "genders", "und", "and", "oder", "or", "für", "for", "mit", "with",
        "in", "im", "der", "die", "das", "the", "a", "als", "vollzeit", "teilzeit", "remote", "hybrid",
    };

    public static bool Matches(Application application, Vacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(vacancy);

        if (application.VacancyId == vacancy.Id)
        {
            return true;
        }

        if (application.DuplicateKey is not null && application.DuplicateKey == vacancy.DuplicateKey)
        {
            return true;
        }

        return SimilarCompany(application.Company, vacancy.Company) && SimilarTitle(application.Position, vacancy.Title);
    }

    public static bool SimilarCompany(string a, string b)
    {
        var x = DuplicateKeys.NormalizeCompany(a);
        var y = DuplicateKeys.NormalizeCompany(b);
        if (x.Length < 3 || y.Length < 3)
        {
            return false;
        }

        return x == y || x.StartsWith(y + " ", StringComparison.Ordinal) || y.StartsWith(x + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// The distinctive words (technologies, focus, place — not „Senior", „Fullstack", „Entwickler") of the shorter
    /// title nearly all occur in the other one, so „… – .NET &amp; Angular oder Vue.js" and „… – Go &amp; Vue.js" at the
    /// same company are different jobs. Titles without distinctive words („Softwareentwickler (m/w/d)") compare all
    /// their words.
    /// </summary>
    public static bool SimilarTitle(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Count == 0 || y.Count == 0)
        {
            return false;
        }

        var dx = x.Where(w => !GenericTitleWords.Contains(w)).ToHashSet(StringComparer.Ordinal);
        var dy = y.Where(w => !GenericTitleWords.Contains(w)).ToHashSet(StringComparer.Ordinal);
        if (dx.Count > 0 && dy.Count > 0)
        {
            var (smallD, largeD) = dx.Count <= dy.Count ? (dx, dy) : (dy, dx);
            return smallD.Count(largeD.Contains) / (double)smallD.Count >= DistinctiveSimilarity;
        }

        var (small, large) = x.Count <= y.Count ? (x, y) : (y, x);
        return small.Count(large.Contains) / (double)small.Count >= TitleSimilarity;
    }

    private const double DistinctiveSimilarity = 0.75;

    /// <summary>Words that most developer titles share; they do not tell two jobs apart.</summary>
    private static readonly HashSet<string> GenericTitleWords = new(StringComparer.Ordinal)
    {
        "senior", "junior", "lead", "fullstack", "full", "stack", "entwickler", "entwicklerin", "developer", "software",
        "softwareentwickler", "softwareentwicklerin", "engineer", "js", "web", "mitarbeiter", "it",
    };

    private static HashSet<string> Words(string title) =>
        DuplicateKeys.NormalizeTitle(title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !TitleNoise.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
}

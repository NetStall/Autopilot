using System.Text;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Extraction;

/// <summary>
/// The rules' match result as text for the „KI-Bewertung" (fitscore.v3, 08c): fulfilled / partial / gaps, each marked
/// must or plus, alternatives, German, reachability and employer type — so the model does not judge the skills again
/// from the raw posting (DPS: „keine Erfahrung mit SQL Server" although I have SQL and MySQL).
/// </summary>
public static partial class FitScoreMatch
{
    public static string Describe(FitResult fit, EmployerType employerType)
    {
        ArgumentNullException.ThrowIfNull(fit);
        var text = new StringBuilder();
        static string Kind(FitRequirement r) => r.Importance <= RequirementImportance.NiceToHave || r.Optional ? "Plus" : "Muss";
        void Line(string label, IEnumerable<FitRequirement> items)
        {
            var list = items.Select(r => $"{r.Name} [{Kind(r)}{(r.Coverage == FitCoverage.Partial ? ", " + r.Note : string.Empty)}]").ToList();
            text.Append(label).Append(": ").Append(list.Count == 0 ? "–" : string.Join(", ", list)).Append('\n');
        }

        var requirements = fit.Requirements.Where(r => r.Importance != RequirementImportance.Alternative).ToList();
        Line("erfüllt", requirements.Where(r => r.Coverage == FitCoverage.Covered));
        Line("teilweise (Erfahrung vorhanden, aber nicht genau das)", requirements.Where(r => r.Coverage == FitCoverage.Partial));
        Line("Lücke", requirements.Where(r => r.Coverage == FitCoverage.Gap));
        var alternatives = fit.Requirements.Where(r => r.Importance == RequirementImportance.Alternative).Select(r => r.Name).ToList();
        text.Append("Alternativen (durch einen anderen Skill erfüllt, keine Lücke): ").Append(alternatives.Count == 0 ? "–" : string.Join(", ", alternatives)).Append('\n');
        text.Append("Deutsch gefordert: ").Append(fit.RequiredGerman?.ToString() ?? "nicht genannt").Append('\n');
        text.Append("Arbeitgeber: ").Append(employerType switch
        {
            EmployerType.Direct => "direkter Arbeitgeber",
            EmployerType.Agency => "Personalvermittlung",
            EmployerType.Temporary => "Zeitarbeit",
            _ => "unbekannt",
        }).Append('\n');
        foreach (var warning in fit.Warnings.Where(w => w.Penalty > 0))
        {
            text.Append("Regel-Abzug: ").Append(warning.Text).Append(" (−").Append(warning.Penalty).Append(")\n");
        }

        text.Append("Erreichbar: ").Append(fit.Unreachable ? "nein" : "ja").Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// The model may only add what the rules cannot see: a blocker that names a plus, an alternative or something I
    /// (partly) have is dropped, and „keine Erfahrung mit X" for a partial X becomes „teilweise Erfahrung mit X" (08c).
    /// </summary>
    public static AiFitScore Check(AiFitScore score, FitResult fit)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(fit);
        var notBlocking = fit.Requirements
            .Where(r => r.Coverage != FitCoverage.Gap || r.Optional || r.Importance <= RequirementImportance.NiceToHave)
            .Select(r => r.Name).ToList();
        var mustGaps = fit.Requirements
            .Where(r => r.Coverage == FitCoverage.Gap && !r.Optional && r.Importance > RequirementImportance.NiceToHave)
            .Select(r => r.Name).ToList();
        bool Names(string text, string name) => text.Contains(name, StringComparison.OrdinalIgnoreCase);
        var blockers = score.Blockers
            .Where(b => !PlusWord().IsMatch(b))
            .Where(b => mustGaps.Any(g => Names(b, g)) || !notBlocking.Any(n => Names(b, n)))
            .ToList();

        var reasons = score.Reasons.Select(reason =>
        {
            foreach (var partial in fit.Requirements.Where(r => r.Coverage is FitCoverage.Partial or FitCoverage.Covered))
            {
                reason = Regex.Replace(reason, $@"[Kk]eine\s+(?:praktische\s+|berufliche\s+)?Erfahrung\s+(mit|in)\s+{Regex.Escape(partial.Name)}",
                    $"teilweise Erfahrung $1 {partial.Name}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            }

            return reason;
        }).ToList();
        return score with { Blockers = blockers, Reasons = reasons };
    }

    [GeneratedRegex(@"idealerweise|von\s+Vorteil|wünschenswert|nice\s+to\s+have|ein\s+Plus|optional|ideally", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex PlusWord();
}

namespace JobAutopilot.Core.Applications;

/// <summary>German labels of <see cref="ApplicationStatus"/> (AGENTS.md) and parsing them back (tracker import).</summary>
public static class ApplicationStatuses
{
    private static readonly Dictionary<ApplicationStatus, string> Labels = new()
    {
        [ApplicationStatus.Draft] = "Entwurf",
        [ApplicationStatus.Approved] = "Freigegeben",
        [ApplicationStatus.Sent] = "Gesendet",
        [ApplicationStatus.Acknowledged] = "Eingangsbestätigung",
        [ApplicationStatus.Response] = "Rückmeldung",
        [ApplicationStatus.Interview] = "Interview",
        [ApplicationStatus.TechInterview] = "Tech-Interview",
        [ApplicationStatus.Offer] = "Angebot",
        [ApplicationStatus.Rejected] = "Absage",
        [ApplicationStatus.NoResponse] = "Keine Antwort",
        [ApplicationStatus.Withdrawn] = "Zurückgezogen",
    };

    /// <summary>Other spellings seen in trackers.</summary>
    private static readonly Dictionary<string, ApplicationStatus> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Eingangsbestaetigung"] = ApplicationStatus.Acknowledged,
        ["Rueckmeldung"] = ApplicationStatus.Response,
        ["Techinterview"] = ApplicationStatus.TechInterview,
        ["Technisches Interview"] = ApplicationStatus.TechInterview,
        ["Zurueckgezogen"] = ApplicationStatus.Withdrawn,
        ["Beworben"] = ApplicationStatus.Sent,
        ["Abgesagt"] = ApplicationStatus.Rejected,
    };

    public static string Label(ApplicationStatus status) => Labels[status];

    public static bool TryParseLabel(string? value, out ApplicationStatus status)
    {
        status = default;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var (key, label) in Labels)
        {
            if (string.Equals(label, text, StringComparison.OrdinalIgnoreCase) || string.Equals(key.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                status = key;
                return true;
            }
        }

        return Aliases.TryGetValue(text, out status);
    }
}

using System.Globalization;

namespace JobAutopilot.Core.Applications;

/// <summary>One row of the candidate's tracker export (German field names, see docs/applications.md).</summary>
public sealed class TrackerEntry
{
    public string? Id { get; init; }

    public string? Datum { get; init; }

    public string? Firma { get; init; }

    public string? Position { get; init; }

    public string? Ort { get; init; }

    public string? Kanal { get; init; }

    public string? Typ { get; init; }

    public string? Status { get; init; }

    public decimal? Gehalt { get; init; }

    public string? Kontakt { get; init; }

    public string? KontaktEmail { get; init; }

    public string? Link { get; init; }

    public string? LetzteAktion { get; init; }

    public string? NaechsterSchritt { get; init; }

    public string? Notizen { get; init; }

    public string? Unterlagen { get; init; }

    public string? Sprache { get; init; }
}

public sealed class TrackerExport
{
    public List<TrackerEntry> Applications { get; init; } = [];
}

/// <summary>A tracker row mapped to application fields, with what could not be mapped.</summary>
public sealed record TrackerMapping(
    string ExternalRef,
    string Company,
    string Position,
    string? Location,
    string? Channel,
    string? ContactName,
    string? ContactEmail,
    string? Url,
    DateTimeOffset AppliedAt,
    DateTimeOffset LastActionAt,
    ApplicationStatus Status,
    int? SalaryExpectation,
    IReadOnlyList<string> Documents,
    string? NextStep,
    string? Notes,
    IReadOnlyList<string> Warnings);

/// <summary>Maps tracker rows; never throws for odd values — they go into the notes and the warnings.</summary>
public static class TrackerMapper
{
    /// <summary>Channel names as used in the UI; matching is case-insensitive.</summary>
    public static readonly IReadOnlyList<string> KnownChannels =
    [
        "Arbeitsagentur", "LinkedIn", "Stepstone", "Indeed", "XING", "DEVjobs.de", "Firmenwebsite", "Initiativbewerbung",
        "E-Mail", "Empfehlung", "Karrieremesse", "Himalayas", "Sonstiges",
    ];

    /// <summary>Returns <c>null</c> (with a reason) for rows without id, company or position.</summary>
    public static (TrackerMapping? Mapping, string? Error) Map(TrackerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = entry.Id?.Trim();
        var company = entry.Firma?.Trim();
        var position = entry.Position?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(company) || string.IsNullOrEmpty(position))
        {
            return (null, $"Eintrag „{id ?? company ?? "?"}\": id, firma und position sind Pflicht – übersprungen.");
        }

        var warnings = new List<string>();
        var notes = new List<string>();

        if (!ApplicationStatuses.TryParseLabel(entry.Status, out var status))
        {
            status = ApplicationStatus.Sent;
            notes.Add($"Status laut Tracker: {entry.Status}");
            warnings.Add($"{id}: unbekannter Status „{entry.Status}\" → Gesendet, Original in den Notizen.");
        }

        string? channel = null;
        if (!string.IsNullOrWhiteSpace(entry.Kanal))
        {
            channel = KnownChannels.FirstOrDefault(c => string.Equals(c, entry.Kanal.Trim(), StringComparison.OrdinalIgnoreCase));
            if (channel is null)
            {
                notes.Add($"Kanal laut Tracker: {entry.Kanal.Trim()}");
                warnings.Add($"{id}: unbekannter Kanal „{entry.Kanal.Trim()}\" – in den Notizen.");
            }
        }

        if (!TryParseDate(entry.Datum, out var applied))
        {
            warnings.Add($"{id}: Datum „{entry.Datum}\" ungültig – heute verwendet.");
            applied = DateTimeOffset.UtcNow;
        }

        var lastAction = TryParseDate(entry.LetzteAktion, out var last) && last >= applied ? last : applied;

        int? salary = null;
        if (entry.Gehalt is { } gehalt)
        {
            if (gehalt >= 1000)
            {
                salary = (int)Math.Round(gehalt);
            }
            else
            {
                notes.Add($"Gehalt laut Tracker: {gehalt.ToString(CultureInfo.InvariantCulture)} (vermutlich €/Stunde)");
                warnings.Add($"{id}: Gehalt {gehalt.ToString(CultureInfo.InvariantCulture)} sieht nach Stundenlohn aus – in den Notizen.");
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Typ))
        {
            notes.Add($"Typ: {entry.Typ.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Sprache))
        {
            notes.Add($"Sprache: {entry.Sprache.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Notizen))
        {
            notes.Add(entry.Notizen.Trim());
        }

        var url = Uri.TryCreate(entry.Link?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

        var documents = (entry.Unterlagen ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return (new TrackerMapping(
            id,
            company,
            position,
            Blank(entry.Ort),
            channel,
            Blank(entry.Kontakt),
            Blank(entry.KontaktEmail),
            url,
            applied,
            lastAction,
            status,
            salary,
            documents,
            Blank(entry.NaechsterSchritt),
            notes.Count == 0 ? null : string.Join("\n", notes),
            warnings), null);
    }

    /// <summary><c>YYYY-MM-DD</c> as noon in Germany (a calendar day, not a moment).</summary>
    public static bool TryParseDate(string? value, out DateTimeOffset date)
    {
        date = default;
        if (!DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return false;
        }

        var noon = day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        date = new DateTimeOffset(noon, TimeZones.Berlin.GetUtcOffset(noon));
        return true;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

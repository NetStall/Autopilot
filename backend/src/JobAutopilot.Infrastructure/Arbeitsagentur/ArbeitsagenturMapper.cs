using System.Globalization;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Infrastructure.Arbeitsagentur;

/// <summary>Maps one Jobsuche search result to a source-neutral <see cref="VacancyCandidate"/>.</summary>
internal static class ArbeitsagenturMapper
{
    internal const string DetailPageBaseUrl = "https://www.arbeitsagentur.de/jobsuche/jobdetail/";
    internal const string UnknownCompany = "Unbekannter Arbeitgeber";

    private static readonly TimeZoneInfo Berlin = FindBerlinTimeZone();

    /// <summary>Returns <c>null</c> for entries that cannot be stored (no reference number or title).</summary>
    public static VacancyCandidate? ToCandidate(JobSearchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var externalId = item.Referenznummer?.Trim();
        var title = item.StellenangebotsTitel?.Trim();
        if (string.IsNullOrEmpty(externalId) || string.IsNullOrEmpty(title)
            || externalId.Length > VacancyLimits.ExternalIdMaxLength)
        {
            return null;
        }

        var company = item.Firma?.Trim();

        return new VacancyCandidate(
            externalId,
            Truncate(title, VacancyLimits.TitleMaxLength),
            Truncate(string.IsNullOrEmpty(company) ? UnknownCompany : company, VacancyLimits.CompanyMaxLength),
            MapLocation(item.Stellenlokationen),
            MapUrl(item.ExterneUrl, externalId),
            ParsePublishedAt(item.Veroeffentlichungszeitraum?.Von ?? item.DatumErsteVeroeffentlichung));
    }

    /// <summary>"{plz} {ort}" of the first location, or <c>null</c> when both are empty.</summary>
    internal static string? MapLocation(IReadOnlyList<JobLocation>? locations)
    {
        var address = locations is { Count: > 0 } ? locations[0].Adresse : null;
        var location = $"{address?.Plz?.Trim()} {address?.Ort?.Trim()}".Trim();
        return location.Length == 0 ? null : Truncate(location, VacancyLimits.LocationMaxLength);
    }

    /// <summary>The employer's own page if it is an absolute https URL, otherwise the public Arbeitsagentur page.</summary>
    internal static string MapUrl(string? externalUrl, string referenceNumber)
    {
        if (Uri.TryCreate(externalUrl?.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsoluteUri.Length <= VacancyLimits.UrlMaxLength)
        {
            return uri.AbsoluteUri;
        }

        return DetailPageBaseUrl + Uri.EscapeDataString(referenceNumber);
    }

    /// <summary>Parses a <c>yyyy-MM-dd</c> date as midnight in Germany (Europe/Berlin).</summary>
    internal static DateTimeOffset? ParsePublishedAt(string? value)
    {
        if (!DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Berlin.GetUtcOffset(midnight));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    // IANA id on Linux/macOS; on Windows without ICU (InvariantGlobalization) only the Windows id resolves.
    private static TimeZoneInfo FindBerlinTimeZone() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Berlin", out var zone)
        || TimeZoneInfo.TryFindSystemTimeZoneById("W. Europe Standard Time", out zone)
            ? zone
            : throw new InvalidOperationException("Time zone Europe/Berlin is not available on this system.");
}

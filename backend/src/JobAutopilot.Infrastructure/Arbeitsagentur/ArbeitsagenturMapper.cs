using System.Globalization;
using JobAutopilot.Core;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Infrastructure.Arbeitsagentur;

/// <summary>Maps one Jobsuche search result to a source-neutral <see cref="VacancyCandidate"/>.</summary>
internal static class ArbeitsagenturMapper
{
    internal const string DetailPageBaseUrl = "https://www.arbeitsagentur.de/jobsuche/jobdetail/";
    internal const string UnknownCompany = "Unbekannter Arbeitgeber";

    private static readonly TimeZoneInfo Berlin = TimeZones.Berlin;

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
            ParsePublishedAt(item.Veroeffentlichungszeitraum?.Von ?? item.DatumErsteVeroeffentlichung),
            RemoteHint: MapRemote(item.Homeofficemoeglich, item.Homeofficeprozent),
            ChangedAt: ParseChangedAt(item.Aenderungsdatum));
    }

    public static VacancyDetails ToDetails(JobDetailsResponse details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var employer = details.IstArbeitnehmerUeberlassung == true ? EmployerType.Temporary
            : details.IstPrivateArbeitsvermittlung == true ? EmployerType.Agency
            // Both flags present and false: posted as the employer itself — a weak signal, see EmployerClassifier.
            : details.IstArbeitnehmerUeberlassung == false && details.IstPrivateArbeitsvermittlung == false ? EmployerType.Direct
            : EmployerType.Unknown;

        return new VacancyDetails(
            NormalizeDescription(details.StellenangebotsBeschreibung),
            MapRemote(details.Homeofficemoeglich, details.Homeofficeprozent),
            employer);
    }

    /// <summary>100 % stated → full remote; any home office (incl. "nach Vereinbarung") → hybrid.</summary>
    internal static RemoteLevel MapRemote(bool? homeOfficePossible, double? homeOfficePercent) =>
        homeOfficePercent >= 100 ? RemoteLevel.FullRemote
        : homeOfficePossible == true || homeOfficePercent > 0 ? RemoteLevel.Hybrid
        : RemoteLevel.Unknown;

    /// <summary>The description is plain text; only line endings and surrounding whitespace are normalized.</summary>
    internal static string? NormalizeDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        return Truncate(normalized, VacancyLimits.DescriptionMaxLength);
    }

    /// <summary><c>aenderungsdatum</c> is German local time without offset, e.g. <c>2026-10-13T09:04:40.708</c>.</summary>
    internal static DateTimeOffset? ParseChangedAt(string? value)
    {
        if (!DateTime.TryParse(value?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Berlin.GetUtcOffset(unspecified));
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

    private static string Truncate(string value, int maxLength) => TextLimits.Truncate(value, maxLength);
}

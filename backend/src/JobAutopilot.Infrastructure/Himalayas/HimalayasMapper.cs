using JobAutopilot.Core;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Infrastructure.Himalayas;

/// <summary>Maps one Himalayas job to a source-neutral <see cref="VacancyCandidate"/>.</summary>
internal static class HimalayasMapper
{
    internal const string UnknownCompany = "Unbekannter Arbeitgeber";

    /// <summary>Region names in <c>locationRestrictions</c> that include applicants living in Germany.</summary>
    private static readonly HashSet<string> RegionsIncludingGermany = new(StringComparer.OrdinalIgnoreCase)
    {
        "Germany", "Deutschland", "DE", "Europe", "EU", "European Union", "EMEA", "DACH", "Worldwide", "Anywhere",
    };

    /// <summary>
    /// Returns <c>null</c> for jobs that cannot be stored (no id or title, no https application link — the terms
    /// require the API's link for applying). A job restricted to countries without Germany (e.g. US-only) is returned
    /// with an <see cref="VacancyCandidate.ExclusionReason"/> and counted as excluded.
    /// </summary>
    public static VacancyCandidate? ToCandidate(HimalayasJob job, string country)
    {
        ArgumentNullException.ThrowIfNull(job);

        var externalId = ExternalIdFrom(job.Guid);
        var title = job.Title?.Trim();
        var url = HttpsUrl(job.ApplicationLink) ?? HttpsUrl(job.Guid);
        if (externalId is null || string.IsNullOrEmpty(title) || url is null)
        {
            return null;
        }

        var exclusion = OpenTo(job.LocationRestrictions, country)
            ? null
            : "Nur für Bewerber aus: " + string.Join(", ", job.LocationRestrictions!.Take(5));

        var company = job.CompanyName?.Trim();
        var description = HtmlText.ToPlainText(job.Description);
        return new VacancyCandidate(
            externalId,
            TextLimits.Truncate(title, VacancyLimits.TitleMaxLength),
            TextLimits.Truncate(string.IsNullOrEmpty(company) ? UnknownCompany : company, VacancyLimits.CompanyMaxLength),
            MapLocation(job.LocationRestrictions),
            url,
            FromUnix(job.PubDate),
            description is null ? null : TextLimits.Truncate(description, VacancyLimits.DescriptionMaxLength),
            RemoteLevel.FullRemote,
            ExpiresAt: FromUnix(job.ExpiryDate),
            ExclusionReason: exclusion);
    }

    /// <summary>The last path segment of the job's permanent URL, e.g. <c>senior-net-developer-4267385201</c>.</summary>
    internal static string? ExternalIdFrom(string? guid)
    {
        if (!Uri.TryCreate(guid?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrEmpty(segment) ? null : TextLimits.Truncate(segment, VacancyLimits.ExternalIdMaxLength);
    }

    /// <summary>The location restrictions as shown in the list, e.g. „Remote (Deutschland, Austria)".</summary>
    internal static string MapLocation(IReadOnlyList<string>? restrictions)
    {
        if (restrictions is not { Count: > 0 })
        {
            return "Remote (weltweit)";
        }

        var names = restrictions.Select(r => IsGermany(r) ? "Deutschland" : r.Trim()).ToList();
        var text = names.Count <= 4
            ? $"Remote ({string.Join(", ", names)})"
            : $"Remote ({names.Count} Länder{(names.Contains("Deutschland") ? " inkl. Deutschland" : string.Empty)})";
        return TextLimits.Truncate(text, VacancyLimits.LocationMaxLength);
    }

    /// <summary>Unix time in seconds; the documentation also mentions milliseconds, so large values are treated as such.</summary>
    internal static DateTimeOffset? FromUnix(long? value) => value switch
    {
        null or <= 0 => null,
        > 100_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds(value.Value),
        _ => DateTimeOffset.FromUnixTimeSeconds(value.Value),
    };

    /// <summary>Worldwide (no restriction), Germany, the configured country, or a region that contains Germany.</summary>
    private static bool OpenTo(IReadOnlyList<string>? restrictions, string country) =>
        restrictions is not { Count: > 0 }
        || restrictions.Any(r => RegionsIncludingGermany.Contains(r.Trim()) || string.Equals(r.Trim(), country, StringComparison.OrdinalIgnoreCase));

    private static bool IsGermany(string value) =>
        value.Trim() is "Germany" or "DE" or "Deutschland";

    private static string? HttpsUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsoluteUri.Length <= VacancyLimits.UrlMaxLength
            ? uri.AbsoluteUri
            : null;
}

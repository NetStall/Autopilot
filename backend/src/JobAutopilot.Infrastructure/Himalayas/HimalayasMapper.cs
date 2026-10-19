using JobAutopilot.Core;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Infrastructure.Himalayas;

/// <summary>Maps one Himalayas job to a source-neutral <see cref="VacancyCandidate"/>.</summary>
internal static class HimalayasMapper
{
    internal const string UnknownCompany = "Unbekannter Arbeitgeber";

    /// <summary>
    /// Returns <c>null</c> for jobs that cannot be stored: no id or title, no https application link (the terms require
    /// the API's link for applying), or a location restriction that excludes Germany.
    /// </summary>
    public static VacancyCandidate? ToCandidate(HimalayasJob job, string country)
    {
        ArgumentNullException.ThrowIfNull(job);

        var externalId = ExternalIdFrom(job.Guid);
        var title = job.Title?.Trim();
        var url = HttpsUrl(job.ApplicationLink) ?? HttpsUrl(job.Guid);
        if (externalId is null || string.IsNullOrEmpty(title) || url is null || !OpenTo(job.LocationRestrictions, country))
        {
            return null;
        }

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
            ExpiresAt: FromUnix(job.ExpiryDate));
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

    internal static string MapLocation(IReadOnlyList<string>? restrictions)
    {
        if (restrictions is not { Count: > 0 })
        {
            return "Remote (weltweit)";
        }

        return restrictions.Count == 1 && IsGermany(restrictions[0])
            ? "Remote (Deutschland)"
            : TextLimits.Truncate($"Remote ({restrictions.Count} Länder inkl. Deutschland)", VacancyLimits.LocationMaxLength);
    }

    /// <summary>Unix time in seconds; the documentation also mentions milliseconds, so large values are treated as such.</summary>
    internal static DateTimeOffset? FromUnix(long? value) => value switch
    {
        null or <= 0 => null,
        > 100_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds(value.Value),
        _ => DateTimeOffset.FromUnixTimeSeconds(value.Value),
    };

    private static bool OpenTo(IReadOnlyList<string>? restrictions, string country) =>
        restrictions is not { Count: > 0 } || restrictions.Any(r => IsGermany(r) || string.Equals(r, country, StringComparison.OrdinalIgnoreCase));

    private static bool IsGermany(string value) =>
        value.Trim() is "Germany" or "DE" or "Deutschland";

    private static string? HttpsUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsoluteUri.Length <= VacancyLimits.UrlMaxLength
            ? uri.AbsoluteUri
            : null;
}

using System.ComponentModel.DataAnnotations;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.AspNetCore.Mvc;

namespace JobAutopilot.Api.Features.Vacancies;

/// <summary>
/// Query string of <c>GET /api/vacancies</c>. Enum values are taken as strings so that unknown or numeric values
/// produce a validation problem naming the parameter instead of a generic 400.
/// </summary>
public sealed class VacancyListQuery : IValidatableObject
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchNames = 20;
    public const int MaxEmployerTypes = 10;
    public const int MaxEnumValues = 10;

    /// <summary>Text in title or company.</summary>
    [FromQuery(Name = "q")]
    [MaxLength(200)]
    public string? Q { get; init; }

    [FromQuery(Name = "location")]
    [MaxLength(200)]
    public string? Location { get; init; }

    /// <summary>Repeatable: <c>?employerType=Direct&amp;employerType=Unknown</c>.</summary>
    [FromQuery(Name = "employerType")]
    [MaxLength(MaxEmployerTypes)]
    public string[]? EmployerType { get; init; }

    [FromQuery(Name = "seen")]
    public bool? Seen { get; init; }

    [FromQuery(Name = "source")]
    public string? Source { get; init; }

    /// <summary>Repeatable remote level: <c>FullRemote</c>, <c>Hybrid</c>, <c>OnSite</c>, <c>Unknown</c>.</summary>
    [FromQuery(Name = "remote")]
    [MaxLength(MaxEnumValues)]
    public string[]? Remote { get; init; }

    /// <summary>Repeatable: <c>Relevant</c>, <c>Maybe</c>, <c>Irrelevant</c>. Unscored vacancies count as Maybe.</summary>
    [FromQuery(Name = "relevance")]
    [MaxLength(MaxEnumValues)]
    public string[]? Relevance { get; init; }

    /// <summary>Repeatable search name: vacancies found by any of them.</summary>
    [FromQuery(Name = "search")]
    [MaxLength(MaxSearchNames)]
    public string[]? Search { get; init; }

    /// <summary>Repeatable: <c>None</c>, <c>Starred</c>, <c>Dismissed</c>. Without it, dismissed vacancies are hidden.</summary>
    [FromQuery(Name = "interest")]
    [MaxLength(MaxEmployerTypes)]
    public string[]? Interest { get; init; }

    /// <summary>Show dismissed vacancies too (only relevant without <c>interest</c>).</summary>
    [FromQuery(Name = "includeDismissed")]
    public bool? IncludeDismissed { get; init; }

    /// <summary>Only vacancies that entered the system at or after this moment, e.g. the start of the last import.</summary>
    [FromQuery(Name = "fetchedSince")]
    public DateTimeOffset? FetchedSince { get; init; }

    /// <summary><c>relevance</c> (default), <c>published</c> or <c>fetched</c>.</summary>
    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    public int PageOrDefault => Page ?? 1;

    public int PageSizeOrDefault => PageSize ?? DefaultPageSize;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var value in EmployerType ?? [])
        {
            if (!TryParseEnum<EmployerType>(value, out _))
            {
                yield return new ValidationResult(
                    $"'{value}' is not an employer type. Allowed: {string.Join(", ", Enum.GetNames<EmployerType>())}.",
                    [nameof(EmployerType)]);
            }
        }

        if (Source is not null && !TryParseEnum<VacancySource>(Source, out _))
        {
            yield return new ValidationResult(
                $"'{Source}' is not a source. Allowed: {string.Join(", ", Enum.GetNames<VacancySource>())}.",
                [nameof(Source)]);
        }

        foreach (var value in Remote ?? [])
        {
            if (!TryParseEnum<RemoteLevel>(value, out _))
            {
                yield return new ValidationResult(
                    $"'{value}' is not a remote level. Allowed: {string.Join(", ", Enum.GetNames<RemoteLevel>())}.",
                    [nameof(Remote)]);
            }
        }

        foreach (var value in Relevance ?? [])
        {
            if (!TryParseEnum<RelevanceLevel>(value, out _))
            {
                yield return new ValidationResult(
                    $"'{value}' is not a relevance level. Allowed: {string.Join(", ", Enum.GetNames<RelevanceLevel>())}.",
                    [nameof(Relevance)]);
            }
        }

        foreach (var value in Interest ?? [])
        {
            if (!TryParseEnum<VacancyInterest>(value, out _))
            {
                yield return new ValidationResult(
                    $"'{value}' is not an interest. Allowed: {string.Join(", ", Enum.GetNames<VacancyInterest>())}.",
                    [nameof(Interest)]);
            }
        }

        if (Search?.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > VacancySearchRules.NameMaxLength) == true)
        {
            yield return new ValidationResult(
                $"Search names must not be empty and at most {VacancySearchRules.NameMaxLength} characters.",
                [nameof(Search)]);
        }

        if (Sort is not null && !TryParseEnum<VacancySort>(Sort, out _))
        {
            yield return new ValidationResult("Sort must be 'relevance', 'published' or 'fetched'.", [nameof(Sort)]);
        }
    }

    /// <summary>Call only after validation succeeded.</summary>
    public VacancyFilter ToFilter() => new(
        Q,
        Location,
        EmployerType is { Length: > 0 }
            ? EmployerType.Select(Parse<EmployerType>).Distinct().ToList()
            : null,
        Seen,
        Source is null ? null : Parse<VacancySource>(Source),
        Sort is null ? VacancySort.Relevance : Parse<VacancySort>(Sort),
        Remote is { Length: > 0 } ? Remote.Select(Parse<RemoteLevel>).Distinct().ToList() : null,
        Search is { Length: > 0 } ? Search.Select(n => n.Trim()).Distinct(StringComparer.Ordinal).ToList() : null,
        Interest is { Length: > 0 } ? Interest.Select(Parse<VacancyInterest>).Distinct().ToList() : null,
        IncludeDismissed ?? false,
        FetchedSince,
        Relevance is { Length: > 0 } ? Relevance.Select(Parse<RelevanceLevel>).Distinct().ToList() : null);

    private static T Parse<T>(string value)
        where T : struct, Enum =>
        TryParseEnum<T>(value, out var result) ? result : throw new InvalidOperationException($"Unvalidated value '{value}'.");

    /// <summary>Case-insensitive names only — numbers like "7" are rejected.</summary>
    private static bool TryParseEnum<T>(string value, out T result)
        where T : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && !char.IsAsciiDigit(value.Trim()[0])
            && !value.Trim().StartsWith('-')
            && Enum.TryParse(value.Trim(), ignoreCase: true, out result)
            && Enum.IsDefined(result);
    }
}

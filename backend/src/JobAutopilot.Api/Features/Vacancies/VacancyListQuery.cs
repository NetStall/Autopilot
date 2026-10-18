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

    /// <summary>Only vacancies found by a remote-only search (<c>true</c>) or only the others (<c>false</c>).</summary>
    [FromQuery(Name = "remote")]
    public bool? Remote { get; init; }

    /// <summary>Repeatable search name: vacancies found by any of them.</summary>
    [FromQuery(Name = "search")]
    [MaxLength(MaxSearchNames)]
    public string[]? Search { get; init; }

    /// <summary><c>published</c> (default) or <c>fetched</c>.</summary>
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

        if (Search?.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > VacancySearchRules.NameMaxLength) == true)
        {
            yield return new ValidationResult(
                $"Search names must not be empty and at most {VacancySearchRules.NameMaxLength} characters.",
                [nameof(Search)]);
        }

        if (Sort is not null && !TryParseEnum<VacancySort>(Sort, out _))
        {
            yield return new ValidationResult("Sort must be 'published' or 'fetched'.", [nameof(Sort)]);
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
        Sort is null ? VacancySort.Published : Parse<VacancySort>(Sort),
        Remote,
        Search is { Length: > 0 } ? Search.Select(n => n.Trim()).Distinct(StringComparer.Ordinal).ToList() : null);

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

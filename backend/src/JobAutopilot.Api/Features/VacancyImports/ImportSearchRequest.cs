using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using JobAutopilot.Core.Import;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>A single ad-hoc search; same shape as an entry of <c>Import:Searches</c>.</summary>
public sealed class ImportSearchRequest : IValidatableObject
{
    public const string DefaultName = "ad-hoc";

    /// <summary>Recorded in the vacancies' matched searches; defaults to <see cref="DefaultName"/>.</summary>
    [MaxLength(VacancySearchRules.NameMaxLength)]
    public string? Name { get; init; }

    [Required]
    [MaxLength(VacancySearchRules.KeywordMaxLength)]
    public string Keyword { get; init; } = string.Empty;

    /// <summary>Omit for all of Germany.</summary>
    [MaxLength(VacancySearchRules.LocationMaxLength)]
    public string? Location { get; init; }

    [Range(0, VacancySearchRules.MaxRadiusKm)]
    public int? RadiusKm { get; init; }

    [Range(0, VacancySearchRules.MaxPublishedWithinDays)]
    public int? PublishedWithinDays { get; init; }

    public bool RemoteOnly { get; init; }

    [Range(1, VacancySearchRules.MaxPagesLimit)]
    public int? MaxPages { get; init; }

    public VacancySearch ToSearch() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim(),
        Keyword = Keyword.Trim(),
        Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
        RadiusKm = RadiusKm,
        PublishedWithinDays = PublishedWithinDays ?? VacancySearch.DefaultPublishedWithinDays,
        RemoteOnly = RemoteOnly,
        MaxPages = MaxPages,
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RadiusKm is not null && string.IsNullOrWhiteSpace(Location))
        {
            yield return new ValidationResult(
                "RadiusKm requires a Location (without Location the search covers all of Germany).",
                [nameof(RadiusKm)]);
        }

        if (!string.IsNullOrWhiteSpace(Name) && !Name.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            yield return new ValidationResult("Name may only contain letters, digits, '-' or '_'.", [nameof(Name)]);
        }
    }

    /// <summary>
    /// Custom binding so that an empty body means "no search" whatever the content type is.
    /// Windows PowerShell's <c>Invoke-RestMethod -Method Post</c> sends <c>application/x-www-form-urlencoded</c>
    /// even without a body, which the default JSON binding rejects with 415.
    /// </summary>
    public static async ValueTask<ImportSearchRequest?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;

        if (request.ContentLength == 0 || (request.ContentLength is null && !request.HasJsonContentType()))
        {
            return null;
        }

        if (!request.HasJsonContentType())
        {
            throw new BadHttpRequestException(
                "The request body must be JSON (Content-Type: application/json).",
                StatusCodes.Status415UnsupportedMediaType);
        }

        var jsonOptions = context.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        try
        {
            return await request.ReadFromJsonAsync<ImportSearchRequest>(jsonOptions, context.RequestAborted);
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException("The request body is not valid JSON.", ex);
        }
    }
}

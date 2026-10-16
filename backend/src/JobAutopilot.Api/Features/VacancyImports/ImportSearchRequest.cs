using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using JobAutopilot.Core.Import;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>A single ad-hoc search; same shape as an entry of <c>Import:Searches</c>.</summary>
public sealed class ImportSearchRequest
{
    [Required]
    [MaxLength(200)]
    public string Keyword { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? Location { get; init; }

    [Range(0, 200)]
    public int? RadiusKm { get; init; }

    [Range(0, 100)]
    public int? PublishedWithinDays { get; init; }

    public VacancySearch ToSearch() =>
        new(Keyword.Trim(), string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(), RadiusKm, PublishedWithinDays);

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

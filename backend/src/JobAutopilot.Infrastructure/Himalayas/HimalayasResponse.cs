using System.Text.Json.Serialization;

namespace JobAutopilot.Infrastructure.Himalayas;

// Response of GET jobs/api/search — only the fields we use. See docs/decisions/0008-second-source-himalayas.md.

internal sealed record HimalayasSearchResponse(
    [property: JsonPropertyName("jobs")] IReadOnlyList<HimalayasJob>? Jobs,
    [property: JsonPropertyName("totalCount")] int TotalCount);

internal sealed record HimalayasJob(
    [property: JsonPropertyName("guid")] string? Guid,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("companyName")] string? CompanyName,
    [property: JsonPropertyName("applicationLink")] string? ApplicationLink,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("locationRestrictions")] IReadOnlyList<string>? LocationRestrictions,
    [property: JsonPropertyName("pubDate")] long? PubDate,
    [property: JsonPropertyName("expiryDate")] long? ExpiryDate);

using System.Text.Json.Serialization;

namespace JobAutopilot.Infrastructure.Arbeitsagentur;

// Response of GET pc/v6/jobs — only the fields we use. See docs/arbeitsagentur-api.md.

internal sealed record JobSearchResponse(
    [property: JsonPropertyName("ergebnisliste")] IReadOnlyList<JobSearchItem>? Ergebnisliste,
    [property: JsonPropertyName("maxErgebnisse")] int MaxErgebnisse,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("size")] int Size);

internal sealed record JobSearchItem(
    [property: JsonPropertyName("referenznummer")] string? Referenznummer,
    [property: JsonPropertyName("stellenangebotsTitel")] string? StellenangebotsTitel,
    [property: JsonPropertyName("firma")] string? Firma,
    [property: JsonPropertyName("stellenlokationen")] IReadOnlyList<JobLocation>? Stellenlokationen,
    [property: JsonPropertyName("veroeffentlichungszeitraum")] JobPeriod? Veroeffentlichungszeitraum,
    [property: JsonPropertyName("datumErsteVeroeffentlichung")] string? DatumErsteVeroeffentlichung,
    [property: JsonPropertyName("externeURL")] string? ExterneUrl);

internal sealed record JobLocation(
    [property: JsonPropertyName("adresse")] JobAddress? Adresse);

internal sealed record JobAddress(
    [property: JsonPropertyName("plz")] string? Plz,
    [property: JsonPropertyName("ort")] string? Ort);

internal sealed record JobPeriod(
    [property: JsonPropertyName("von")] string? Von);

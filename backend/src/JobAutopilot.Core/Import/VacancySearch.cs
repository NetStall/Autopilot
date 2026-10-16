namespace JobAutopilot.Core.Import;

/// <summary>One search against a vacancy source, e.g. ".NET Entwickler" within 50 km of Deggendorf.</summary>
public sealed record VacancySearch(string Keyword, string? Location, int? RadiusKm, int? PublishedWithinDays);

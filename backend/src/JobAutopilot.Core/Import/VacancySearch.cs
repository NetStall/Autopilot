using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>
/// One search against a vacancy source, e.g. "C#" within 70 km of Deggendorf, or ".NET" fully remote anywhere
/// in Germany. <see cref="Name"/> identifies the search; vacancies remember which searches found them.
/// </summary>
public sealed record VacancySearch
{
    public const int DefaultPublishedWithinDays = 30;

    /// <summary>Short unique label, e.g. <c>region-csharp</c> or <c>remote-dotnet</c>.</summary>
    public required string Name { get; init; }

    public required string Keyword { get; init; }

    /// <summary>Where to search: <see cref="VacancySource.Arbeitsagentur"/> (default) or <see cref="VacancySource.Himalayas"/>.</summary>
    public VacancySource Source { get; init; } = VacancySource.Arbeitsagentur;

    /// <summary>Place to search around; <c>null</c> = all of Germany.</summary>
    public string? Location { get; init; }

    /// <summary>Radius around <see cref="Location"/>; not allowed without a location.</summary>
    public int? RadiusKm { get; init; }

    public int PublishedWithinDays { get; init; } = DefaultPublishedWithinDays;

    /// <summary>Only home-office (remote) vacancies. Vacancies found this way are marked as remote.</summary>
    public bool RemoteOnly { get; init; }

    /// <summary>Overrides the source's global page limit for this search.</summary>
    public int? MaxPages { get; init; }
}

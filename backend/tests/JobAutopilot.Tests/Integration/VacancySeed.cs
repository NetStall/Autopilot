using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Integration;

/// <summary>Builds vacancies with sensible defaults for tests that insert straight into the database.</summary>
public static class VacancySeed
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static int _counter;

    public static Vacancy Make(
        string title = "Softwareentwickler (m/w/d)",
        string company = "Beispiel GmbH",
        string? location = "93047 Regensburg",
        EmployerType employerType = EmployerType.Unknown,
        VacancySource source = VacancySource.Arbeitsagentur,
        bool isSeen = false,
        DateTimeOffset? publishedAt = null,
        int fetchedMinutesAfterBase = 0)
    {
        var n = Interlocked.Increment(ref _counter);
        var fetchedAt = BaseTime.AddMinutes(fetchedMinutesAfterBase);
        return new Vacancy
        {
            Id = Guid.CreateVersion7(fetchedAt),
            Source = source,
            ExternalId = $"test-{n}-{Guid.NewGuid():N}",
            Title = title,
            Company = company,
            Location = location,
            Url = $"https://example.de/jobs/{n}",
            EmployerType = employerType,
            PublishedAt = publishedAt,
            FetchedAt = fetchedAt,
            IsSeen = isSeen,
        };
    }
}

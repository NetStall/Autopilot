using System.Runtime.CompilerServices;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Fakes;

/// <summary>Returns canned candidates per keyword and records which searches were run.</summary>
public sealed class FakeVacancySource : IVacancySource
{
    private readonly Dictionary<string, List<VacancyCandidate>> _resultsByKeyword = new(StringComparer.Ordinal);

    public VacancySource Source => VacancySource.Arbeitsagentur;

    public List<VacancySearch> Searches { get; } = [];

    /// <summary>When set, every search fails with <see cref="VacancySourceUnavailableException"/>.</summary>
    public bool IsUnavailable { get; set; }

    public FakeVacancySource Returns(string keyword, params VacancyCandidate[] candidates)
    {
        _resultsByKeyword[keyword] = [.. candidates];
        return this;
    }

    public async IAsyncEnumerable<VacancyCandidate> SearchAsync(
        VacancySearch search,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Searches.Add(search);
        await Task.Yield();

        if (IsUnavailable)
        {
            throw new VacancySourceUnavailableException("Fake source is down.");
        }

        foreach (var candidate in _resultsByKeyword.GetValueOrDefault(search.Keyword) ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return candidate;
        }
    }

    public static VacancyCandidate Candidate(string externalId, string title = "Softwareentwickler (m/w/d)") =>
        new(externalId, title, "Beispiel GmbH", "94469 Deggendorf", $"https://example.de/jobs/{externalId}",
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(2)));
}

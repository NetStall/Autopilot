using System.Runtime.CompilerServices;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Fakes;

/// <summary>Returns canned candidates per search name (or keyword) and records which searches were run.</summary>
public sealed class FakeVacancySource : IVacancySource
{
    private readonly Dictionary<string, List<VacancyCandidate>> _results = new(StringComparer.Ordinal);

    public VacancySource Source => VacancySource.Arbeitsagentur;

    public List<VacancySearch> Searches { get; } = [];

    /// <summary>When set, every search fails with <see cref="VacancySourceUnavailableException"/>.</summary>
    public bool IsUnavailable { get; set; }

    /// <param name="searchNameOrKeyword">Matched against the search's name first, then its keyword.</param>
    public FakeVacancySource Returns(string searchNameOrKeyword, params VacancyCandidate[] candidates)
    {
        _results[searchNameOrKeyword] = [.. candidates];
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

        var candidates = _results.GetValueOrDefault(search.Name) ?? _results.GetValueOrDefault(search.Keyword) ?? [];
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return candidate;
        }
    }

    public static VacancyCandidate Candidate(string externalId, string title = "Softwareentwickler (m/w/d)") =>
        new(externalId, title, "Beispiel GmbH", "94469 Deggendorf", $"https://example.de/jobs/{externalId}",
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(2)));
}

using System.Runtime.CompilerServices;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Fakes;

/// <summary>Returns canned candidates per search name (or keyword) and records which searches were run.</summary>
public sealed class FakeVacancySource(VacancySource source = VacancySource.Arbeitsagentur) : IVacancySource
{
    private readonly Dictionary<string, List<VacancyCandidate>> _results = new(StringComparer.Ordinal);

    public VacancySource Source => source;

    public List<VacancySearch> Searches { get; } = [];

    /// <summary>When set, each search waits for it before returning results (to observe a running import).</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Completed when the first search has started.</summary>
    public TaskCompletionSource FirstSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        FirstSearchStarted.TrySetResult();
        await Task.Yield();

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

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
        new(externalId, title, "Beispiel GmbH", "94469 Talstadt", $"https://example.de/jobs/{externalId}",
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(2)));
}

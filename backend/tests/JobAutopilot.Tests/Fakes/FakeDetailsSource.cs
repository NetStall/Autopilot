using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Fakes;

/// <summary>Returns canned details per external id; unknown ids are "gone" (<c>null</c>).</summary>
public sealed class FakeDetailsSource(VacancySource source = VacancySource.Arbeitsagentur) : IVacancyDetailsSource
{
    private readonly Dictionary<string, VacancyDetails> _details = new(StringComparer.Ordinal);

    public VacancySource Source => source;

    public List<string> Requested { get; } = [];

    /// <summary>Fail with <see cref="VacancySourceUnavailableException"/> from this request number on (1-based).</summary>
    public int? FailFromRequest { get; set; }

    public FakeDetailsSource Returns(string externalId, VacancyDetails details)
    {
        _details[externalId] = details;
        return this;
    }

    public Task<VacancyDetails?> GetDetailsAsync(string externalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requested.Add(externalId);
        if (FailFromRequest is { } failFrom && Requested.Count >= failFrom)
        {
            throw new VacancySourceUnavailableException("Fake details source is down.");
        }

        return Task.FromResult(_details.GetValueOrDefault(externalId));
    }
}

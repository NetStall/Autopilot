using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Derives everything that is computed from a vacancy's text: remote level and relevance (score, level, reasons).
/// Called by the import (new and refreshed vacancies), after fetching details, and when re-scoring.
/// </summary>
public sealed class VacancyEnricher(RelevanceScorer scorer, RemoteClassifier remoteClassifier)
{
    /// <param name="remoteHint">What the source says about home office, if anything.</param>
    public void Apply(Vacancy vacancy, RemoteLevel remoteHint = RemoteLevel.Unknown)
    {
        ArgumentNullException.ThrowIfNull(vacancy);

        var remote = remoteClassifier.Classify(vacancy.Title, vacancy.Description);
        vacancy.RaiseRemoteLevel(remoteHint > remote.Level ? remoteHint : remote.Level);

        var relevance = scorer.Score(vacancy.Title, vacancy.Description);
        vacancy.RelevanceScore = relevance.Score;
        vacancy.RelevanceLevel = relevance.Level;

        var reasons = relevance.Reasons.ToList();
        if (RemoteReason(vacancy.RemoteLevel, remote) is { } remoteReason)
        {
            reasons.Add(remoteReason);
        }

        vacancy.RelevanceReasons = reasons;
    }

    /// <summary>The phrase found now, or — when the stored level is higher — where that level came from.</summary>
    private static string? RemoteReason(RemoteLevel stored, RemoteClassification found)
    {
        if (stored == found.Level)
        {
            return found.Reason;
        }

        return stored switch
        {
            RemoteLevel.FullRemote => "Voll remote laut Quelle",
            RemoteLevel.Hybrid => "Homeoffice möglich (Remote-Suche bzw. Angabe der Quelle)",
            _ => null,
        };
    }
}

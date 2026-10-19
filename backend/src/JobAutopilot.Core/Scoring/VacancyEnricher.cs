using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Derives everything that is computed from a vacancy's text: duplicate key, remote level, employer type and
/// relevance (score, level, reasons). Called by the import (new and refreshed vacancies), after fetching details, and when re-scoring.
/// </summary>
public sealed class VacancyEnricher(
    RelevanceScorer scorer,
    RemoteClassifier remoteClassifier,
    EmployerClassifier employerClassifier)
{
    /// <param name="remoteHint">What the source says about home office, if anything.</param>
    /// <param name="employerHint">What the source says about the employer (Arbeitsagentur flags), if anything.</param>
    public void Apply(
        Vacancy vacancy,
        RemoteLevel remoteHint = RemoteLevel.Unknown,
        EmployerType employerHint = EmployerType.Unknown)
    {
        ArgumentNullException.ThrowIfNull(vacancy);

        vacancy.DuplicateKey = DuplicateKeys.For(vacancy.Company, vacancy.Title);
        var remote = remoteClassifier.Classify(vacancy.Title, vacancy.Description);
        vacancy.RaiseRemoteLevel(remoteHint > remote.Level ? remoteHint : remote.Level);

        var employerReason = ApplyEmployer(vacancy, employerHint);

        var relevance = scorer.Score(vacancy.Title, vacancy.Description);
        vacancy.RelevanceScore = relevance.Score;
        vacancy.RelevanceLevel = relevance.Level;

        var reasons = relevance.Reasons.ToList();
        if (RemoteReason(vacancy.RemoteLevel, remote) is { } remoteReason)
        {
            reasons.Add(remoteReason);
        }

        if (employerReason is not null)
        {
            reasons.Add(employerReason);
        }

        vacancy.RelevanceReasons = reasons;
    }

    /// <summary>
    /// Manual vacancies keep the type Vitalii chose. Otherwise a detected type wins; when nothing is detected now
    /// (e.g. re-scoring without the source's flags), the stored type stays.
    /// </summary>
    private string? ApplyEmployer(Vacancy vacancy, EmployerType hint)
    {
        if (vacancy.Source == VacancySource.Manual)
        {
            return null;
        }

        var employer = employerClassifier.Classify(vacancy.Company, vacancy.Description, hint);
        if (employer.Type != EmployerType.Unknown)
        {
            vacancy.EmployerType = employer.Type;
            return employer.Reason;
        }

        return vacancy.EmployerType switch
        {
            EmployerType.Temporary => "Zeitarbeit laut Quelle",
            EmployerType.Agency => "Personalvermittlung laut Quelle",
            EmployerType.Direct => "Direkter Arbeitgeber laut Quelle",
            _ => null,
        };
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

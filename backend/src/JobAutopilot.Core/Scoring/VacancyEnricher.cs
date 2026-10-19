using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Derives everything that is computed from a vacancy's text: duplicate key, remote level, employer type and
/// relevance (score, level, reasons). Called by the import (new and refreshed vacancies), after fetching details, and when re-scoring.
/// </summary>
public sealed class VacancyEnricher(
    IRelevanceScorerProvider scorers,
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
        vacancy.RaiseRemoteHint(remoteHint);
        var remote = remoteClassifier.Classify(vacancy.Title, vacancy.Description);
        vacancy.RemoteLevel = CombineRemote(remote.Level, vacancy.RemoteHint);

        var employerReason = ApplyEmployer(vacancy, employerHint);

        var relevance = scorers.Scorer.Score(vacancy.Title, vacancy.Description);
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

    /// <summary>
    /// An explicit phrase decides (full remote or hybrid — so a wrong „100 %" field cannot beat „anteilig Homeoffice");
    /// „kein Homeoffice" cannot undo a remote-search hit or the source's home-office flag (then hybrid); without any
    /// phrase the source's data count.
    /// </summary>
    internal static RemoteLevel CombineRemote(RemoteLevel fromText, RemoteLevel hint) => fromText switch
    {
        RemoteLevel.FullRemote or RemoteLevel.Hybrid => fromText,
        RemoteLevel.OnSite => hint >= RemoteLevel.Hybrid ? RemoteLevel.Hybrid : RemoteLevel.OnSite,
        _ => hint,
    };

    /// <summary>The phrase that decided, or — when the level comes from the source — that.</summary>
    private static string? RemoteReason(RemoteLevel level, RemoteClassification found)
    {
        if (level == found.Level)
        {
            return found.Reason;
        }

        return level switch
        {
            RemoteLevel.FullRemote => "Voll remote laut Quelle",
            RemoteLevel.Hybrid => "Homeoffice möglich (Remote-Suche bzw. Angabe der Quelle)",
            _ => null,
        };
    }
}

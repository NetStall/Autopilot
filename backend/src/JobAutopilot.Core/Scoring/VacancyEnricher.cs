using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Derives everything that is computed from a vacancy's text: duplicate key, remote level, employer type and
/// relevance (score, level, reasons). Called by the import (new and refreshed vacancies), after fetching details, and when re-scoring.
/// </summary>
/// <param name="onRemoteConflict">Called when the source's home-office field and the text disagree (vacancy id, the
/// source used) — the API logs it; Core has no logging.</param>
public sealed class VacancyEnricher(
    IRelevanceScorerProvider scorers,
    RemoteClassifier remoteClassifier,
    EmployerClassifier employerClassifier,
    VacancyFitService? fit = null,
    Action<Guid, RemoteSource>? onRemoteConflict = null,
    SearchProfiles.ISearchProfileSource? searchProfiles = null)
{

    /// <summary>The badge for a hybrid job outside my places without a stated share (07c); the remote note starts with it.</summary>
    public const string ByArrangement = "Remote nur nach Absprache";

    /// <param name="remoteHint">What the source says about home office, if anything.</param>
    /// <param name="employerHint">What the source says about the employer (Arbeitsagentur flags), if anything.</param>
    public void Apply(
        Vacancy vacancy,
        RemoteLevel remoteHint = RemoteLevel.Unknown,
        EmployerType employerHint = EmployerType.Unknown)
    {
        ArgumentNullException.ThrowIfNull(vacancy);

        vacancy.DuplicateKey = DuplicateKeys.For(vacancy.Company, vacancy.Title);
        // Himalayas lists remote jobs for a country („Remote (Deutschland)"): remote, not hybrid (07a2).
        var remoteListing = vacancy.Source == VacancySource.Himalayas && vacancy.Location?.StartsWith("Remote", StringComparison.OrdinalIgnoreCase) == true;
        if (remoteListing)
        {
            remoteHint = RemoteLevel.FullRemote;
        }

        vacancy.RaiseRemoteHint(remoteHint);
        var extraction = Extraction.StoredExtraction.ForDescription(vacancy.ExtractionJson, vacancy.Description);
        var remote = remoteClassifier.Classify(vacancy.Title, vacancy.Description);
        var partialCue = remoteClassifier.FindPartial(vacancy.Title, vacancy.Description) ?? RemoteAssessor.OnSiteDays(vacancy.Description);
        var assessment = RemoteAssessor.Assess(
            remote, partialCue, HomeOfficeShare.Find(vacancy.Description), vacancy.RemoteHint, vacancy.HomeOfficePercent, extraction?.Result, remoteListing);
        // „Nur Regeln" (07c): the same assessment without the model's answer.
        var rulesAssessment = extraction is null ? assessment : RemoteAssessor.Assess(
            remote, partialCue, HomeOfficeShare.Find(vacancy.Description), vacancy.RemoteHint, vacancy.HomeOfficePercent, null, remoteListing);
        vacancy.RemoteLevel = assessment.Level;
        vacancy.RemotePartial = assessment.Partial;
        vacancy.RemoteNote = assessment.Label;
        if (assessment.Conflict is not null)
        {
            onRemoteConflict?.Invoke(vacancy.Id, assessment.Source);
        }

        // Himalayas lists the hiring company itself: direct unless agency signals say otherwise.
        if (employerHint == EmployerType.Unknown && vacancy.Source == VacancySource.Himalayas)
        {
            employerHint = EmployerType.Direct;
        }

        var employerReason = ApplyEmployer(vacancy, employerHint);
        var german = GermanRequirementDetector.Detect(vacancy.Title, vacancy.Description);
        vacancy.RequiredGerman = german.Category == RequiredGerman.Unknown && extraction is not null
            ? FromExtraction(extraction.Result)
            : german.Category;

        var relevance = scorers.Scorer.Score(vacancy.Title, vacancy.Description);
        vacancy.RelevanceScore = relevance.Score;
        vacancy.RelevanceLevel = relevance.Level;

        var reasons = relevance.Reasons.ToList();
        if (RemoteReason(assessment, remote, partialCue) is { } remoteReason)
        {
            reasons.Add(remoteReason);
        }

        if (employerReason is not null)
        {
            reasons.Add(employerReason);
        }

        if (german.Reason is { } germanReason)
        {
            reasons.Add(germanReason);
        }

        if (extraction is not null)
        {
            reasons.Add($"Anforderungen: KI ({extraction.Model}, {extraction.PromptVersion})");
        }

        // The ranking: relevance adjusted by the fit warnings against the profile.
        var fitResult = fit?.Analyze(vacancy) ?? FitResult.Empty;
        vacancy.RankScore = Math.Clamp(relevance.Score - fitResult.Penalty, 0, 100);
        vacancy.Unreachable = fitResult.Unreachable;
        if (fitResult.RemoteByArrangement)
        {
            vacancy.RemoteNote = assessment.Label is { Length: > 0 } label ? $"{ByArrangement} – {label}" : ByArrangement;
        }
        vacancy.RuleRankScore = extraction is null || fit is null
            ? vacancy.RankScore
            : Math.Clamp(relevance.Score - fit.AnalyzeRules(vacancy, rulesAssessment.Level, rulesAssessment.Partial).Penalty, 0, 100);
        foreach (var warning in fitResult.Warnings.Where(w => w.Penalty > 0))
        {
            reasons.Add($"Fit: {warning.Text} (−{warning.Penalty})");
        }

        if (fitResult.Penalty > 0)
        {
            reasons.Add($"Rang {vacancy.RankScore} = Relevanz {relevance.Score} − {fitResult.Penalty}");
        }

        // Layer 1 (08b): hard filters after scoring (they use the German level and reachability).
        var profile = searchProfiles?.Current.Profile;
        var filtered = profile is null ? null : Filters.HardFilters.Check(vacancy, profile.HardFilters, profile.HideUnreachable);
        vacancy.FilterReason = filtered?.Reason;
        vacancy.FilterNote = filtered?.Note;

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

        var employer = employerClassifier.Classify(vacancy.Company, vacancy.Description, hint, SourceName(vacancy.Source));
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

    /// <summary>The German bucket from the model when the phrases found nothing.</summary>
    private static RequiredGerman FromExtraction(Extraction.RequirementExtraction extraction) => extraction switch
    {
        { GermanNotRequired: true } => RequiredGerman.None,
        { GermanLevel: Profile.LanguageLevel.C1 or Profile.LanguageLevel.C2 or Profile.LanguageLevel.Native } => RequiredGerman.C1Plus,
        { GermanLevel: Profile.LanguageLevel.B2 } => RequiredGerman.B2,
        { GermanLevel: not null } => RequiredGerman.B1OrLower,
        _ => RequiredGerman.Unknown,
    };

    private static string SourceName(VacancySource source) => source switch
    {
        VacancySource.Himalayas => "Himalayas (einstellende Firma)",
        _ => "Arbeitsagentur",
    };

    /// <summary>
    /// The phrase that decided (as before), or the assessment's label; a partial cue and a conflict between field and
    /// text are named, so „Warum?" shows what was used.
    /// </summary>
    private static string? RemoteReason(RemoteAssessment assessment, RemoteClassification found, string? partialCue)
    {
        var reason = assessment switch
        {
            { Level: RemoteLevel.Unknown } => null,
            { Source: RemoteSource.Text, Partial: true } when partialCue is not null => $"{assessment.Label}: „{partialCue}“",
            { Source: RemoteSource.Text } when assessment.Level == found.Level && found.Reason is not null => found.Reason,
            { Source: RemoteSource.Flag, Level: RemoteLevel.FullRemote } when found.Level == RemoteLevel.Hybrid =>
                $"Voll remote laut Quelle ({found.Reason} ist nur ein Angebot)",
            { Source: RemoteSource.Flag, Level: RemoteLevel.FullRemote } => "Voll remote laut Quelle",
            _ => assessment.Label,
        };
        return reason is not null && assessment.Conflict is { } conflict ? $"{reason} – Widerspruch: {conflict}" : reason;
    }
}

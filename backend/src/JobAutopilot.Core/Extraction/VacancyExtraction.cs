namespace JobAutopilot.Core.Extraction;

public enum ExtractionStatus
{
    Succeeded,

    /// <summary>Invalid JSON twice (first answer and the one retry); the rules stay in charge for this text.</summary>
    Failed,
}

/// <summary>
/// Cache of extractions (step 07a): one row per description hash, model and prompt version, so the same text is never
/// sent twice — also not when the same posting comes from two sources. A failed attempt is kept too, so a text the
/// model cannot handle is not retried in every run (a new model or prompt version tries again).
/// </summary>
public sealed class VacancyExtraction
{
    public const int ModelMaxLength = 100;

    public const int PromptVersionMaxLength = 40;

    public const int ErrorMaxLength = 1000;

    public Guid Id { get; set; }

    public string DescriptionHash { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public ExtractionStatus Status { get; set; }

    /// <summary>The validated <see cref="RequirementExtraction"/> as JSON; <c>null</c> when it failed.</summary>
    public string? ResultJson { get; set; }

    /// <summary>The validation errors of the last attempt (English, from <see cref="ExtractionValidator"/>).</summary>
    public string? Error { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    /// <summary>Wall time of all attempts together.</summary>
    public long DurationMs { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// „Passt" / „Passt nicht" for one vacancy (step 07a): my own judgement, the ground truth the rules and the model are
/// measured against in step 07b. One per vacancy; changing it updates the timestamp.
/// </summary>
public sealed class GoldLabel
{
    public Guid VacancyId { get; set; }

    public bool Fits { get; set; }

    public DateTimeOffset LabeledAt { get; set; }
}

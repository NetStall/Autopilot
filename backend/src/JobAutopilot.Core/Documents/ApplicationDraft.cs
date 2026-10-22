namespace JobAutopilot.Core.Documents;

public enum DraftRevisionKind
{
    /// <summary>The whole letter generated („Unterlagen erstellen", „Neu erstellen").</summary>
    Generated,

    /// <summary>One paragraph rewritten by the model.</summary>
    Rephrased,

    /// <summary>Saved by hand.</summary>
    Saved,

    /// <summary>An older revision restored.</summary>
    Restored,

    /// <summary>The package texts (e-mail body, motivation, Über mich) generated (08c) — one model call, letter unchanged.</summary>
    Package,
}

/// <summary>
/// The documents of one application in the making: the letter (structured), the CV choices, the fact guard's
/// warnings and what the model calls cost. Personal data — never logged. One draft per application; every
/// generation and every manual save is kept as an <see cref="ApplicationDraftRevision"/>.
/// </summary>
public class ApplicationDraft
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public required LetterDraft Letter { get; set; }

    public CvOptions CvOptions { get; set; } = CvOptions.Default;

    public List<DraftWarning> Warnings { get; set; } = [];

    /// <summary>Prompt template of the last generation, e.g. <c>letter.v1</c>.</summary>
    public required string PromptVersion { get; set; }

    public required string Model { get; set; }

    /// <summary>Tokens of all model calls for this draft (generations and rephrasings).</summary>
    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int Calls { get; set; }

    /// <summary>Optimistic concurrency: each save increments it; a save with an older value is rejected.</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set by „Freigeben"; cleared by any later change (the change needs a new approval).</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>The approval was given despite open warnings (explicitly confirmed).</summary>
    public bool WarningsAccepted { get; set; }

    /// <summary>File names (not paths) of the PDFs written on approval.</summary>
    public string? LetterFile { get; set; }

    public string? CvFile { get; set; }

    public List<ApplicationDraftRevision> Revisions { get; set; } = [];

    /// <summary>„Bewerbungspaket" (08c): the channel texts; <c>null</c> until the package was created.</summary>
    public PackageTexts? Package { get; set; }

    public TokenUsage Usage => new(InputTokens, OutputTokens);

    public bool HasOpenWarnings => Warnings.Any(w => w.Severity == WarningSeverity.Warning);

    /// <summary>Applies a new state, counts tokens, keeps a revision and invalidates an earlier approval.</summary>
    public ApplicationDraftRevision Record(
        DraftRevisionKind kind, LetterDraft letter, CvOptions cvOptions, DateTimeOffset now,
        TokenUsage? usage = null, string? model = null, string? promptVersion = null, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(letter);
        ArgumentNullException.ThrowIfNull(cvOptions);
        Letter = letter;
        CvOptions = cvOptions;
        if (usage is not null)
        {
            InputTokens += usage.InputTokens;
            OutputTokens += usage.OutputTokens;
            Calls++;
        }

        Model = model ?? Model;
        PromptVersion = promptVersion ?? PromptVersion;
        Version++;
        UpdatedAt = now;
        ApprovedAt = null;
        WarningsAccepted = false;

        var revision = new ApplicationDraftRevision
        {
            Id = Guid.CreateVersion7(now),
            DraftId = Id,
            Number = Revisions.Count == 0 ? 1 : Revisions.Max(r => r.Number) + 1,
            Kind = kind,
            Letter = letter,
            CvOptions = cvOptions,
            Model = usage is null ? null : model,
            PromptVersion = usage is null ? null : promptVersion,
            InputTokens = usage?.InputTokens ?? 0,
            OutputTokens = usage?.OutputTokens ?? 0,
            Note = note,
            CreatedAt = now,
        };
        Revisions.Add(revision);
        return revision;
    }
}

/// <summary>One state of a draft: after a generation, a rephrasing, a manual save or a restore.</summary>
public class ApplicationDraftRevision
{
    public Guid Id { get; set; }

    public Guid DraftId { get; set; }

    /// <summary>1, 2, 3 … per draft.</summary>
    public int Number { get; set; }

    public DraftRevisionKind Kind { get; set; }

    public required LetterDraft Letter { get; set; }

    public CvOptions CvOptions { get; set; } = CvOptions.Default;

    /// <summary>Model and tokens of the call that produced this revision (generations and rephrasings only).</summary>
    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsModelCall => Kind is DraftRevisionKind.Generated or DraftRevisionKind.Rephrased;
}

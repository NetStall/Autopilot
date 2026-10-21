using System.ComponentModel.DataAnnotations;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using JobAutopilot.Infrastructure.Documents;

namespace JobAutopilot.Api.Features.Documents;

/// <summary>The letter as sent to and from the review screen (plain text).</summary>
public sealed class LetterDto
{
    [Required]
    [MaxLength(LetterDraft.MaxLineLength)]
    public string Subject { get; init; } = string.Empty;

    [Required]
    [MaxLength(LetterDraft.MaxLineLength)]
    public string Salutation { get; init; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(LetterDraft.MaxParagraphs)]
    public List<string> Paragraphs { get; init; } = [];

    [Required]
    [MaxLength(LetterDraft.MaxLineLength)]
    public string Closing { get; init; } = string.Empty;

    public Formality Formality { get; init; }

    public DocumentLanguage Language { get; init; }

    [MaxLength(100)]
    public List<string> FactsUsed { get; init; } = [];

    [MaxLength(50)]
    public List<string> GapsMentioned { get; init; } = [];

    public static LetterDto From(LetterDraft letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return new()
        {
            Subject = letter.Subject,
            Salutation = letter.Salutation,
            Paragraphs = [.. letter.Paragraphs],
            Closing = letter.Closing,
            Formality = letter.Formality,
            Language = letter.Language,
            FactsUsed = [.. letter.FactsUsed],
            GapsMentioned = [.. letter.GapsMentioned],
        };
    }

    /// <summary>Trimmed; paragraphs without inner line breaks; each paragraph at most <see cref="LetterDraft.MaxParagraphLength"/>.</summary>
    public LetterDraft? ToLetter(out string? error)
    {
        var paragraphs = Paragraphs.Select(p => string.Join(' ', (p ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim())
            .Where(p => p.Length > 0)
            .ToList();
        error = paragraphs.Count == 0 ? "Das Anschreiben braucht mindestens einen Absatz."
            : paragraphs.Any(p => p.Length > LetterDraft.MaxParagraphLength) ? $"Ein Absatz darf höchstens {LetterDraft.MaxParagraphLength} Zeichen haben."
            : null;
        return error is null
            ? new LetterDraft(Subject.Trim(), Salutation.Trim(), paragraphs, Closing.Trim(), Language, Formality, [.. FactsUsed], [.. GapsMentioned])
            : null;
    }
}

public sealed class CvOptionsDto
{
    [MaxLength(30)]
    public string? Focus { get; init; }

    [MaxLength(CvOptions.MaxIds)]
    public List<string> HiddenBullets { get; init; } = [];

    [MaxLength(CvOptions.MaxIds)]
    public List<string> ShownBullets { get; init; } = [];

    [MaxLength(CvOptions.MaxIds)]
    public List<string> BulletOrder { get; init; } = [];

    [MaxLength(30)]
    public List<string> HiddenSkillGroups { get; init; } = [];

    [MaxLength(30)]
    public List<string> SkillGroupOrder { get; init; } = [];

    public static CvOptionsDto From(CvOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new()
        {
            Focus = options.Focus,
            HiddenBullets = [.. options.HiddenBullets],
            ShownBullets = [.. options.ShownBullets],
            BulletOrder = [.. options.BulletOrder],
            HiddenSkillGroups = [.. options.HiddenSkillGroups],
            SkillGroupOrder = [.. options.SkillGroupOrder],
        };
    }

    public CvOptions ToOptions() => new(
        string.IsNullOrWhiteSpace(Focus) ? null : Focus.Trim(),
        Ids(HiddenBullets), Ids(ShownBullets), Ids(BulletOrder), Ids(HiddenSkillGroups), Ids(SkillGroupOrder));

    private static List<string> Ids(List<string> values) =>
        [.. values.Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 100).Select(v => v.Trim()).Distinct(StringComparer.Ordinal)];
}

public sealed class SaveDocumentsRequest
{
    public int Version { get; init; }

    [Required]
    public LetterDto? Letter { get; init; }

    [Required]
    public CvOptionsDto? CvOptions { get; init; }
}

public sealed class VersionRequest
{
    public int Version { get; init; }
}

public sealed class RephraseRequest
{
    public int Version { get; init; }

    [MaxLength(200)]
    public string? Hint { get; init; }
}

public sealed class RestoreRequest
{
    public int Version { get; init; }

    [Range(1, int.MaxValue)]
    public int Revision { get; init; }
}

public sealed class ApproveRequest
{
    public int Version { get; init; }

    /// <summary>Explicit confirmation to approve despite open warnings.</summary>
    public bool AcceptWarnings { get; init; }
}

public sealed class PreviewRequest
{
    public LetterDto? Letter { get; init; }

    public CvOptionsDto? CvOptions { get; init; }
}

public sealed class MarkSentRequest
{
    /// <summary>How I submitted it, e.g. „E-Mail", „Bewerbungsportal", „Arbeitsagentur".</summary>
    [Required]
    [MaxLength(100)]
    public string Channel { get; init; } = string.Empty;

    public DateTimeOffset? At { get; init; }
}

public sealed record CvBulletDto(string Id, string Text, int Score, bool Hidden, bool AutoHidden);

public sealed record CvExperienceDto(string Id, string Title, string Company, string? Where, string Period, IReadOnlyList<CvBulletDto> Bullets);

public sealed record CvSkillGroupDto(string Name, IReadOnlyList<string> Skills, bool Hidden);

public sealed record CvDto(
    DocumentLanguage Language,
    string Focus,
    IReadOnlyList<string> AvailableFocuses,
    string RoleLine,
    string? Summary,
    IReadOnlyList<CvExperienceDto> Experience,
    IReadOnlyList<CvSkillGroupDto> SkillGroups,
    int UntranslatedTexts)
{
    public static CvDto From(CvDocument cv)
    {
        ArgumentNullException.ThrowIfNull(cv);
        return new(cv.Language, cv.Focus, cv.AvailableFocuses, cv.RoleLine, cv.Summary,
            [.. cv.Experience.Select(e => new CvExperienceDto(e.Id, e.Title, e.Company, e.Where, e.Period,
                [.. e.Bullets.Select(b => new CvBulletDto(b.Id, b.Text, b.Score, b.Hidden, b.AutoHidden))]))],
            [.. cv.SkillGroups.Select(g => new CvSkillGroupDto(g.Name, g.Skills, g.Hidden))],
            cv.UntranslatedTexts);
    }
}

public sealed record DraftUsageDto(int InputTokens, int OutputTokens, int Calls, decimal CostUsd, int TokenBudget, int CallsToday, int DailyCallLimit);

public sealed record DraftRevisionDto(int Number, DraftRevisionKind Kind, DateTimeOffset CreatedAt, string? Note, string? Model, int InputTokens, int OutputTokens);

/// <summary>Everything the review screen needs (GET /api/applications/{id}/documents).</summary>
public sealed record DocumentsDto(
    Guid ApplicationId,
    Guid? VacancyId,
    string Company,
    string Position,
    ApplicationStatus Status,
    string StatusLabel,
    int Version,
    LetterDto Letter,
    CvOptionsDto CvOptions,
    CvDto Cv,
    IReadOnlyList<DraftWarning> Warnings,
    bool HasOpenWarnings,
    DateTimeOffset? ApprovedAt,
    bool WarningsAccepted,
    string? LetterFile,
    string? CvFile,
    int LetterPages,
    int CvPages,
    string Model,
    string PromptVersion,
    DraftUsageDto Usage,
    bool ClaudeConfigured,
    IReadOnlyList<CheatSheetItem> CheatSheet,
    IReadOnlyList<DraftRevisionDto> Revisions,
    DateTimeOffset UpdatedAt,
    string SenderName = "")
{
    public static DocumentsDto From(DraftView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var (application, draft) = (view.Application, view.Draft);
        return new(
            application.Id, application.VacancyId, application.Company, application.Position, application.Status,
            ApplicationStatuses.Label(application.Status), draft.Version, LetterDto.From(draft.Letter), CvOptionsDto.From(draft.CvOptions),
            CvDto.From(view.Cv), draft.Warnings, draft.HasOpenWarnings, draft.ApprovedAt, draft.WarningsAccepted,
            draft.LetterFile, draft.CvFile, view.LetterPages, view.CvPages, draft.Model, draft.PromptVersion,
            new DraftUsageDto(draft.InputTokens, draft.OutputTokens, draft.Calls, view.CostUsd, view.DraftTokenBudget, view.CallsToday, view.DailyCallLimit),
            view.ClaudeConfigured, view.CheatSheet,
            [.. draft.Revisions.OrderByDescending(r => r.Number).Select(r => new DraftRevisionDto(r.Number, r.Kind, r.CreatedAt, r.Note, r.Model, r.InputTokens, r.OutputTokens))],
            draft.UpdatedAt,
            view.Cv.Name);
    }
}

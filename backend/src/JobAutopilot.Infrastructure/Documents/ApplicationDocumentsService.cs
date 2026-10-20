using System.Diagnostics;
using JobAutopilot.Core;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Claude;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Documents;

public enum DocumentProblem
{
    NotFound,

    /// <summary>Version conflict or a state that does not allow the step (e.g. already sent).</summary>
    Conflict,

    Invalid,

    /// <summary>Daily call limit or the draft's token budget reached.</summary>
    LimitReached,

    /// <summary>profile.json missing or invalid — no facts, no letter.</summary>
    ProfileMissing,

    /// <summary>The model call failed; see <see cref="DocumentException.GenerationError"/>.</summary>
    GenerationFailed,
}

/// <summary>A refused documents step. The message is German and safe to show.</summary>
public sealed class DocumentException : Exception
{
    public DocumentException()
    {
    }

    public DocumentException(string message)
        : base(message)
    {
    }

    public DocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DocumentException(DocumentProblem problem, string message, LetterGenerationError? generationError = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Problem = problem;
        GenerationError = generationError;
    }

    public DocumentProblem Problem { get; }

    public LetterGenerationError? GenerationError { get; }
}

/// <summary>Everything the review screen shows for one application.</summary>
public sealed record DraftView(
    Application Application,
    ApplicationDraft Draft,
    CvDocument Cv,
    IReadOnlyList<CheatSheetItem> CheatSheet,
    int LetterPages,
    int CvPages,
    decimal CostUsd,
    int CallsToday,
    int DailyCallLimit,
    int DraftTokenBudget,
    bool ClaudeConfigured);

/// <summary>
/// „Unterlagen erstellen" and the review: creates the draft (Claude letter + deterministic CV), saves edits with
/// optimistic concurrency, rewrites single paragraphs, restores revisions, renders previews, approves (writes the
/// PDFs) and records „Als gesendet markieren". Limits: <c>Claude:DailyCallLimit</c> per day and
/// <c>Claude:DraftTokenBudget</c> per draft. A failed model call stores nothing. Logs ids, kinds and token counts only.
/// </summary>
public sealed partial class ApplicationDocumentsService(
    AppDbContext db,
    ILetterGenerator generator,
    IDocumentRenderer renderer,
    FitAnalyzer fitAnalyzer,
    ICandidateProfileSource profiles,
    ISearchProfileSource searchProfiles,
    DocumentFileStore files,
    IOptions<ClaudeOptions> claudeOptions,
    IOptions<FitOptions> fitOptions,
    TimeProvider timeProvider,
    ILogger<ApplicationDocumentsService> logger)
{
    private readonly ClaudeOptions _claude = claudeOptions.Value;

    /// <summary>„Unterlagen erstellen" on a vacancy: the existing draft, or a new application (Draft) with a new draft.</summary>
    public async Task<(DraftView View, bool Created)> CreateForVacancyAsync(Guid vacancyId, CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vacancyId, cancellationToken)
            ?? throw new DocumentException(DocumentProblem.NotFound, "Stelle nicht gefunden.");
        var application = await db.Applications.Include(a => a.Events)
            .Where(a => a.VacancyId == vacancyId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (application is not null)
        {
            var existing = await LoadDraftAsync(application.Id, withRevisions: true, cancellationToken);
            if (existing is not null)
            {
                return (await ViewAsync(application, existing, vacancy, cancellationToken), false);
            }

            if (!ApplicationWorkflow.CanPrepare(application))
            {
                throw new DocumentException(DocumentProblem.Conflict,
                    $"Für diese Stelle gibt es bereits eine Bewerbung im Status „{ApplicationStatuses.Label(application.Status)}“.");
            }
        }

        var now = timeProvider.GetUtcNow();
        var isNew = application is null;
        application ??= ApplicationWorkflow.NewDraft(vacancy, now);
        var draft = await GenerateNewDraftAsync(application, vacancy, cancellationToken);
        if (isNew)
        {
            db.Applications.Add(application);
        }

        db.ApplicationDrafts.Add(draft);
        await SaveAsync(cancellationToken);
        return (await ViewAsync(application, draft, vacancy, cancellationToken), true);
    }

    /// <summary>„Unterlagen erstellen" on an existing application (Draft/Approved) without documents yet.</summary>
    public async Task<(DraftView View, bool Created)> CreateForApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await LoadApplicationAsync(applicationId, cancellationToken);
        var vacancy = await VacancyOfAsync(application, cancellationToken);
        var existing = await LoadDraftAsync(applicationId, withRevisions: true, cancellationToken);
        if (existing is not null)
        {
            return (await ViewAsync(application, existing, vacancy, cancellationToken), false);
        }

        if (!ApplicationWorkflow.CanPrepare(application))
        {
            throw new DocumentException(DocumentProblem.Conflict,
                $"Die Bewerbung ist bereits im Status „{ApplicationStatuses.Label(application.Status)}“ – Unterlagen werden nur für Entwürfe erstellt.");
        }

        var draft = await GenerateNewDraftAsync(application, vacancy, cancellationToken);
        db.ApplicationDrafts.Add(draft);
        await SaveAsync(cancellationToken);
        return (await ViewAsync(application, draft, vacancy, cancellationToken), true);
    }

    public async Task<DraftView> GetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await LoadApplicationAsync(applicationId, cancellationToken);
        var draft = await RequireDraftAsync(applicationId, cancellationToken);
        return await ViewAsync(application, draft, await VacancyOfAsync(application, cancellationToken), cancellationToken);
    }

    /// <summary>„Neu erstellen": a new letter from the model (one call); the CV choices stay.</summary>
    public Task<DraftView> RegenerateAsync(Guid applicationId, int expectedVersion, CancellationToken cancellationToken) =>
        ChangeAsync(applicationId, expectedVersion, requireCall: true, async (context, draft, now) =>
        {
            var generation = await CallAsync(() => generator.GenerateAsync(context.Input, cancellationToken), cancellationToken);
            draft.Record(DraftRevisionKind.Generated, generation.Letter, draft.CvOptions, now, generation.Usage, generation.Model, generation.PromptVersion);
            LogCall(logger, "generate", draft.ApplicationId, draft.Id, generation.Usage.InputTokens, generation.Usage.OutputTokens);
        }, cancellationToken);

    /// <summary>„Absatz neu formulieren": one call, only that paragraph changes.</summary>
    public Task<DraftView> RephraseAsync(Guid applicationId, int expectedVersion, int paragraph, string? hint, CancellationToken cancellationToken) =>
        ChangeAsync(applicationId, expectedVersion, requireCall: true, async (context, draft, now) =>
        {
            if (paragraph < 0 || paragraph >= draft.Letter.Paragraphs.Count)
            {
                throw new DocumentException(DocumentProblem.Invalid, "Diesen Absatz gibt es nicht.");
            }

            var result = await CallAsync(() => generator.RephraseParagraphAsync(context.Input, draft.Letter, paragraph, hint, cancellationToken), cancellationToken);
            var paragraphs = draft.Letter.Paragraphs.ToList();
            paragraphs[paragraph] = result.Paragraph;
            var letter = draft.Letter with
            {
                Paragraphs = paragraphs,
                FactsUsed = [.. draft.Letter.FactsUsed.Union(result.FactsUsed.Where(id => context.Input.Facts.Any(f => f.Id == id)))],
                GapsMentioned = [.. draft.Letter.GapsMentioned.Union(result.GapsMentioned)],
            };
            draft.Record(DraftRevisionKind.Rephrased, letter, draft.CvOptions, now, result.Usage, result.Model, result.PromptVersion,
                note: $"Absatz {paragraph + 1}");
            LogCall(logger, "rephrase", draft.ApplicationId, draft.Id, result.Usage.InputTokens, result.Usage.OutputTokens);
        }, cancellationToken);

    /// <summary>A manual save of letter and CV choices.</summary>
    public Task<DraftView> SaveAsync(Guid applicationId, int expectedVersion, LetterDraft letter, CvOptions cvOptions, CancellationToken cancellationToken) =>
        ChangeAsync(applicationId, expectedVersion, requireCall: false, (_, draft, now) =>
        {
            draft.Record(DraftRevisionKind.Saved, letter with { Language = draft.Letter.Language }, cvOptions, now);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Restores an older revision as a new one (history is never rewritten).</summary>
    public Task<DraftView> RestoreAsync(Guid applicationId, int expectedVersion, int revisionNumber, CancellationToken cancellationToken) =>
        ChangeAsync(applicationId, expectedVersion, requireCall: false, (_, draft, now) =>
        {
            var revision = draft.Revisions.FirstOrDefault(r => r.Number == revisionNumber)
                ?? throw new DocumentException(DocumentProblem.NotFound, $"Version {revisionNumber} gibt es nicht.");
            draft.Record(DraftRevisionKind.Restored, revision.Letter, revision.CvOptions, now, note: $"Version {revisionNumber} wiederhergestellt");
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>„Freigeben": writes both PDFs and sets the application to Approved. Open warnings need <paramref name="acceptWarnings"/>.</summary>
    public async Task<DraftView> ApproveAsync(Guid applicationId, int expectedVersion, bool acceptWarnings, CancellationToken cancellationToken)
    {
        var application = await LoadApplicationAsync(applicationId, cancellationToken);
        var draft = await RequireDraftAsync(applicationId, cancellationToken);
        CheckVersion(draft, expectedVersion);
        var vacancy = await VacancyOfAsync(application, cancellationToken);
        var context = BuildContext(application, vacancy);
        var state = Evaluate(context, draft);
        draft.Warnings = [.. state.Warnings];

        var now = timeProvider.GetUtcNow();
        var personName = context.Profile.Personal.Name;
        var language = draft.Letter.Language;
        var letterFile = DocumentFileStore.FileName(personName, language, letter: true, application.Company);
        var cvFile = DocumentFileStore.FileName(personName, state.Cv.Language, letter: false, application.Company);
        try
        {
            draft.LetterFile = letterFile;
            draft.CvFile = cvFile;
            ApplicationWorkflow.Approve(application, draft, now, acceptWarnings);
        }
        catch (WorkflowException ex)
        {
            throw new DocumentException(DocumentProblem.Conflict, ex.Message, innerException: ex);
        }

        var letterPdf = renderer.RenderLetter(Print(context, draft.Letter, state.Cv), RenderFormat.Pdf);
        var cvPdf = renderer.RenderCv(state.Cv, RenderFormat.Pdf);
        await files.WriteAsync(application.Id, new Dictionary<string, byte[]>
        {
            [letterFile] = letterPdf.Content,
            [cvFile] = cvPdf.Content,
        }, cancellationToken);

        draft.Version++;
        draft.UpdatedAt = now;
        AddNewEvents(application);
        await SaveAsync(cancellationToken);
        LogApproved(logger, application.Id, draft.Id, draft.WarningsAccepted);
        return await ViewAsync(application, draft, context, state, cancellationToken);
    }

    /// <summary>„Als gesendet markieren" — the only way to Sent; requires the approval.</summary>
    public async Task<DraftView> MarkSentAsync(Guid applicationId, string channel, DateTimeOffset? at, CancellationToken cancellationToken)
    {
        var application = await LoadApplicationAsync(applicationId, cancellationToken);
        var draft = await RequireDraftAsync(applicationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        try
        {
            ApplicationWorkflow.MarkSent(application, draft, channel, at ?? now, now);
        }
        catch (WorkflowException ex)
        {
            throw new DocumentException(DocumentProblem.Conflict, ex.Message, innerException: ex);
        }

        AddNewEvents(application);
        await SaveAsync(cancellationToken);
        return await ViewAsync(application, draft, await VacancyOfAsync(application, cancellationToken), cancellationToken);
    }

    /// <summary>PNG of the first page for the live preview — of the posted (unsaved) state, or the saved one.</summary>
    public async Task<RenderedDocument> PreviewAsync(Guid applicationId, bool letter, LetterDraft? unsavedLetter, CvOptions? unsavedCv, CancellationToken cancellationToken)
    {
        var application = await LoadApplicationAsync(applicationId, cancellationToken);
        var draft = await RequireDraftAsync(applicationId, cancellationToken);
        var context = BuildContext(application, await VacancyOfAsync(application, cancellationToken));
        var cv = FitCv(context, unsavedCv ?? draft.CvOptions).Cv;
        return letter
            ? renderer.RenderLetter(Print(context, (unsavedLetter ?? draft.Letter) with { Language = draft.Letter.Language }, cv), RenderFormat.Png)
            : renderer.RenderCv(cv, RenderFormat.Png);
    }

    /// <summary>An approved PDF (written by <see cref="ApproveAsync"/>).</summary>
    public async Task<(string FileName, byte[] Content)?> GetPdfAsync(Guid applicationId, bool letter, CancellationToken cancellationToken)
    {
        var draft = await db.ApplicationDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.ApplicationId == applicationId, cancellationToken);
        var name = letter ? draft?.LetterFile : draft?.CvFile;
        if (draft?.ApprovedAt is null || name is null)
        {
            return null;
        }

        var content = await files.ReadAsync(applicationId, name, cancellationToken);
        return content is null ? null : (name, content);
    }

    private async Task<DraftView> ChangeAsync(
        Guid applicationId, int expectedVersion, bool requireCall,
        Func<DraftContext, ApplicationDraft, DateTimeOffset, Task> change, CancellationToken cancellationToken)
    {
        var application = await LoadApplicationAsync(applicationId, cancellationToken);
        if (!ApplicationWorkflow.CanPrepare(application))
        {
            throw new DocumentException(DocumentProblem.Conflict,
                $"Die Bewerbung ist im Status „{ApplicationStatuses.Label(application.Status)}“ – die Unterlagen sind nicht mehr änderbar.");
        }

        var draft = await RequireDraftAsync(applicationId, cancellationToken);
        CheckVersion(draft, expectedVersion);
        var vacancy = await VacancyOfAsync(application, cancellationToken);
        var context = BuildContext(application, vacancy);
        if (requireCall)
        {
            await CheckLimitsAsync(draft, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        await change(context, draft, now);
        var state = Evaluate(context, draft);
        draft.Warnings = [.. state.Warnings];
        ApplicationWorkflow.Reopen(application, now);
        foreach (var revision in draft.Revisions.Where(r => db.Entry(r).State == EntityState.Detached))
        {
            db.ApplicationDraftRevisions.Add(revision);
        }

        AddNewEvents(application);
        await SaveAsync(cancellationToken);
        return await ViewAsync(application, draft, context, state, cancellationToken);
    }

    private async Task<ApplicationDraft> GenerateNewDraftAsync(Application application, Vacancy? vacancy, CancellationToken cancellationToken)
    {
        var context = BuildContext(application, vacancy);
        await CheckDailyLimitAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var watch = Stopwatch.StartNew();
        LetterGeneration generation;
        try
        {
            generation = await generator.GenerateAsync(context.Input, cancellationToken);
        }
        catch (LetterGenerationException ex)
        {
            throw new DocumentException(DocumentProblem.GenerationFailed, ex.Message, ex.Error, ex);
        }

        var draft = new ApplicationDraft
        {
            Id = Guid.CreateVersion7(now),
            ApplicationId = application.Id,
            Letter = generation.Letter,
            PromptVersion = generation.PromptVersion,
            Model = generation.Model,
            CreatedAt = now,
        };
        draft.Record(DraftRevisionKind.Generated, generation.Letter, CvOptions.Default, now,
            generation.Usage, generation.Model, generation.PromptVersion);
        draft.Version = 1;
        draft.Warnings = [.. Evaluate(context, draft).Warnings];
        LogCall(logger, "generate", application.Id, draft.Id, generation.Usage.InputTokens, generation.Usage.OutputTokens);
        LogDuration(logger, application.Id, watch.ElapsedMilliseconds);
        return draft;
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await call();
        }
        catch (LetterGenerationException ex)
        {
            throw new DocumentException(DocumentProblem.GenerationFailed, ex.Message, ex.Error, ex);
        }
    }

    private async Task CheckLimitsAsync(ApplicationDraft draft, CancellationToken cancellationToken)
    {
        if (draft.Usage.Total >= _claude.DraftTokenBudget)
        {
            throw new DocumentException(DocumentProblem.LimitReached,
                $"Das Token-Budget dieses Entwurfs ({_claude.DraftTokenBudget:N0} Tokens) ist aufgebraucht – bitte von Hand bearbeiten.");
        }

        await CheckDailyLimitAsync(cancellationToken);
    }

    private async Task CheckDailyLimitAsync(CancellationToken cancellationToken)
    {
        if (await CallsTodayAsync(cancellationToken) >= _claude.DailyCallLimit)
        {
            throw new DocumentException(DocumentProblem.LimitReached,
                $"Das Tageslimit von {_claude.DailyCallLimit} Claude-Aufrufen ist erreicht (Claude:DailyCallLimit).");
        }
    }

    /// <summary>Model calls since midnight in Germany (generations and rephrasings are revisions).</summary>
    private Task<int> CallsTodayAsync(CancellationToken cancellationToken)
    {
        var now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZones.Berlin);
        var midnight = new DateTimeOffset(now.Date, now.Offset).ToUniversalTime();
        return db.ApplicationDraftRevisions.CountAsync(
            r => (r.Kind == DraftRevisionKind.Generated || r.Kind == DraftRevisionKind.Rephrased) && r.CreatedAt >= midnight,
            cancellationToken);
    }

    private sealed record DraftContext(
        CandidateProfile Profile,
        LetterVacancy Vacancy,
        VacancySource Source,
        LetterInput Input,
        SearchProfile SearchProfile,
        DateOnly Today);

    private sealed record DraftState(CvDocument Cv, int CvPages, int LetterPages, IReadOnlyList<DraftWarning> Warnings);

    private DraftContext BuildContext(Application application, Vacancy? vacancy)
    {
        var profile = profiles.Current.Usable
            ?? throw new DocumentException(DocumentProblem.ProfileMissing,
                "Das Profil (data/profile.json) fehlt oder ist ungültig – ohne Profil keine Unterlagen. Details auf der Seite „Profil“.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZones.Berlin).DateTime);
        var searchProfile = searchProfiles.Current.Profile;
        var letterVacancy = vacancy is null
            ? new LetterVacancy(application.Position, application.Company, application.Location, null, null)
            : new LetterVacancy(vacancy.Title, vacancy.Company, vacancy.Location, vacancy.Description, vacancy.Source.ToString());
        var fit = fitAnalyzer.Analyze(
            profile,
            new FitInput(letterVacancy.Title, letterVacancy.Description, vacancy?.EmployerType ?? EmployerType.Unknown, letterVacancy.Location, vacancy?.RemoteLevel ?? RemoteLevel.Unknown),
            today,
            FitSettings.From(searchProfile));
        var input = LetterFactsBuilder.Build(profile, letterVacancy, fit, searchProfile, today);
        return new DraftContext(profile, letterVacancy, vacancy?.Source ?? VacancySource.Manual, input, searchProfile, today);
    }

    /// <summary>CV fitted to one page, page counts and all warnings (fact guard + CV checks).</summary>
    private DraftState Evaluate(DraftContext context, ApplicationDraft draft)
    {
        var (cv, cvPages) = FitCv(context, draft.CvOptions);
        var letterPages = renderer.RenderLetter(Print(context, draft.Letter, cv), RenderFormat.Png).Pages;
        var known = fitOptions.Value.CommonRequirements.SelectMany(r => r.Terms.Append(r.Name));
        var warnings = FactGuard.Check(draft.Letter, context.Input, context.Profile, known, letterPages).ToList();
        if (cvPages > 1)
        {
            warnings.Add(new DraftWarning(DraftWarning.CvTooLong, $"Der Lebenslauf hat {cvPages} Seiten – Stichpunkte ausblenden."));
        }

        if (cv.Language == DocumentLanguage.De && cv.UntranslatedTexts > 0)
        {
            warnings.Add(new DraftWarning(DraftWarning.CvUntranslated,
                $"Lebenslauf: {cv.UntranslatedTexts} Texte ohne deutsche Fassung im Profil – unverändert übernommen (Felder *De in profile.json).",
                Severity: WarningSeverity.Hint));
        }

        return new DraftState(cv, cvPages, letterPages, warnings);
    }

    /// <summary>
    /// Builds the CV and, while it is longer than one page, hides the weakest automatically shown bullet (never one I
    /// chose to show) — „always one page".
    /// </summary>
    private (CvDocument Cv, int Pages) FitCv(DraftContext context, CvOptions options)
    {
        var language = context.Input.Tone.Language;
        var extraHidden = new List<string>();
        while (true)
        {
            var effective = options with { HiddenBullets = [.. options.HiddenBullets, .. extraHidden] };
            var cv = CvBuilder.Build(context.Profile, context.Vacancy.Title, context.Vacancy.Description, language, effective);
            cv = cv with
            {
                Experience = [.. cv.Experience.Select(e => e with
                {
                    Bullets = [.. e.Bullets.Select(b => extraHidden.Contains(b.Id) ? b with { AutoHidden = true } : b)],
                })],
            };
            var pages = renderer.RenderCv(cv, RenderFormat.Png).Pages;
            var candidate = cv.Experience
                .SelectMany(e => e.Visible.Skip(1))
                .Where(b => !options.ShownBullets.Contains(b.Id))
                .OrderBy(b => b.Score)
                .ThenByDescending(b => b.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (pages <= 1 || candidate is null || extraHidden.Count >= 20)
            {
                return (cv, pages);
            }

            extraHidden.Add(candidate.Id);
        }
    }

    private static LetterPrint Print(DraftContext context, LetterDraft letter, CvDocument cv) =>
        LetterPrint.Create(context.Profile, letter, context.Vacancy, context.Input.Tone, cv.RoleLine, context.Today);

    private Task<DraftView> ViewAsync(Application application, ApplicationDraft draft, Vacancy? vacancy, CancellationToken cancellationToken) =>
        ViewAsync(application, draft, BuildContext(application, vacancy), null, cancellationToken);

    private async Task<DraftView> ViewAsync(
        Application application, ApplicationDraft draft, DraftContext context, DraftState? evaluated, CancellationToken cancellationToken)
    {
        var state = evaluated ?? Evaluate(context, draft);
        var cheatSheet = CheatSheet.Build(context.Profile, draft.Letter, context.Vacancy, context.Source, context.Input.Tone, context.SearchProfile, context.Today);
        if (draft.Revisions.Count == 0 && db.Entry(draft).State != EntityState.Added)
        {
            await db.Entry(draft).Collection(d => d.Revisions).LoadAsync(cancellationToken);
        }

        return new DraftView(
            application, draft, state.Cv, cheatSheet, state.LetterPages, state.CvPages,
            _claude.EstimateCostUsd(draft.InputTokens, draft.OutputTokens),
            await CallsTodayAsync(cancellationToken), _claude.DailyCallLimit, _claude.DraftTokenBudget, _claude.IsConfigured);
    }

    private async Task<Application> LoadApplicationAsync(Guid applicationId, CancellationToken cancellationToken) =>
        await db.Applications.Include(a => a.Events).SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new DocumentException(DocumentProblem.NotFound, "Bewerbung nicht gefunden.");

    private Task<ApplicationDraft?> LoadDraftAsync(Guid applicationId, bool withRevisions, CancellationToken cancellationToken)
    {
        var query = db.ApplicationDrafts.AsQueryable();
        if (withRevisions)
        {
            query = query.Include(d => d.Revisions);
        }

        return query.SingleOrDefaultAsync(d => d.ApplicationId == applicationId, cancellationToken);
    }

    private async Task<ApplicationDraft> RequireDraftAsync(Guid applicationId, CancellationToken cancellationToken) =>
        await LoadDraftAsync(applicationId, withRevisions: true, cancellationToken)
            ?? throw new DocumentException(DocumentProblem.NotFound, "Für diese Bewerbung gibt es noch keine Unterlagen.");

    private async Task<Vacancy?> VacancyOfAsync(Application application, CancellationToken cancellationToken) =>
        application.VacancyId is { } id
            ? await db.Vacancies.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, cancellationToken)
            : null;

    private static void CheckVersion(ApplicationDraft draft, int expectedVersion)
    {
        if (draft.Version != expectedVersion)
        {
            throw new DocumentException(DocumentProblem.Conflict,
                "Die Unterlagen wurden inzwischen an anderer Stelle geändert. Bitte neu laden.");
        }
    }

    /// <summary>Status events added by the workflow must be inserted explicitly (the collection is loaded tracked).</summary>
    private void AddNewEvents(Application application)
    {
        foreach (var added in application.Events.Where(e => db.Entry(e).State == EntityState.Detached))
        {
            db.ApplicationEvents.Add(added);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new DocumentException(DocumentProblem.Conflict, "Die Unterlagen wurden gleichzeitig geändert. Bitte neu laden.", innerException: ex);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new DocumentException(DocumentProblem.Conflict, "Die Unterlagen werden gerade schon erstellt. Bitte neu laden.", innerException: ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Draft {Operation} for application {ApplicationId}, draft {DraftId}: {InputTokens} input / {OutputTokens} output tokens")]
    private static partial void LogCall(ILogger logger, string operation, Guid applicationId, Guid draftId, int inputTokens, int outputTokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Documents for application {ApplicationId} created in {ElapsedMs} ms")]
    private static partial void LogDuration(ILogger logger, Guid applicationId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Application {ApplicationId} approved (draft {DraftId}, warnings accepted: {WarningsAccepted})")]
    private static partial void LogApproved(ILogger logger, Guid applicationId, Guid draftId, bool warningsAccepted);
}

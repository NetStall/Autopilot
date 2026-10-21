using JobAutopilot.Core.Extraction;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Extraction;

/// <summary>Body of <c>PUT /api/vacancies/{id}/label</c>.</summary>
public sealed record GoldLabelRequest(bool? Fits);

public sealed record GoldLabelDto(Guid VacancyId, bool Fits, DateTimeOffset LabeledAt);

/// <summary>How many of the current Top N still need an extraction, and how long that takes at the measured speed.</summary>
public sealed record PendingExtractionsDto(int Top, int Pending, double SecondsPerVacancy, int EstimatedSeconds);

/// <summary>„Bewertet: 12 / 30" in the header.</summary>
public sealed record GoldLabelSummaryDto(int Labeled, int Fits, int NotFits, int Target);

public static class ExtractionEndpoints
{
    /// <summary>Labels wanted before the evaluation of step 07b makes sense.</summary>
    public const int LabelTarget = 30;

    public static IEndpointRouteBuilder MapExtractionEndpoints(this IEndpointRouteBuilder app)
    {
        var extractions = app.MapGroup("/api/extractions").WithTags("Extraction");
        extractions.MapGet("/", GetStatus).WithName("GetExtractionStatus");
        extractions.MapPost("/", StartBackfill).WithName("StartExtraction");
        extractions.MapDelete("/current", CancelRun).WithName("CancelExtraction");
        extractions.MapGet("/pending", GetPending).WithName("GetPendingExtractions");
        extractions.MapGet("/failed", GetFailed).WithName("GetFailedExtractions");

        app.MapPost("/api/vacancies/{id:guid}/extract", ExtractVacancy).WithTags("Extraction").WithName(nameof(ExtractVacancy));

        app.MapPut("/api/vacancies/{id:guid}/label", SetLabel).WithTags("Gold labels").WithName(nameof(SetLabel));
        app.MapDelete("/api/vacancies/{id:guid}/label", DeleteLabel).WithTags("Gold labels").WithName(nameof(DeleteLabel));
        app.MapGet("/api/labels/summary", GetLabelSummary).WithTags("Gold labels").WithName(nameof(GetLabelSummary));
        return app;
    }

    private static Ok<ExtractionStatusDto> GetStatus(ExtractionCoordinator coordinator, VacancyExtractionService extraction) =>
        TypedResults.Ok(coordinator.Status(extraction.IsAvailable, extraction.Model));

    /// <summary>Choices of „Top N" in the UI; 100 by default.</summary>
    public static readonly int[] TopChoices = [50, 100, 200, 500];

    /// <summary>Until a run has measured it: s/vacancy of qwen3:8b with thinking off on a 12 GB GPU (07a2).</summary>
    public const double DefaultSecondsPerVacancy = 4;

    /// <summary>„Alle analysieren" for the current Top N (50 / 100 / 200 / 500) — only what is not analysed yet.</summary>
    private static Results<Accepted<ExtractionStatusDto>, ProblemHttpResult> StartBackfill(
        ExtractionCoordinator coordinator, VacancyExtractionService extraction, int? top)
    {
        if (!extraction.IsAvailable)
        {
            return NotAvailable();
        }

        if (top is { } n && !TopChoices.Contains(n))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiges Top N.", detail: $"Erlaubt: {string.Join(", ", TopChoices)}.");
        }

        coordinator.RequestBackfill(top ?? 100);
        return TypedResults.Accepted("/api/extractions", coordinator.Status(true, extraction.Model));
    }

    private static async Task<Ok<PendingExtractionsDto>> GetPending(
        ExtractionCoordinator coordinator, VacancyExtractionService extraction, int? top, CancellationToken cancellationToken)
    {
        var n = top is { } t && TopChoices.Contains(t) ? t : 100;
        var pending = extraction.IsAvailable ? (await extraction.PendingAsync(n, cancellationToken)).Count : 0;
        var speed = coordinator.Status(extraction.IsAvailable, extraction.Model).LastSecondsPerVacancy ?? DefaultSecondsPerVacancy;
        return TypedResults.Ok(new PendingExtractionsDto(n, pending, speed, (int)Math.Ceiling(pending * speed)));
    }

    private static async Task<Ok<IReadOnlyList<FailedExtraction>>> GetFailed(VacancyExtractionService extraction, CancellationToken cancellationToken) =>
        TypedResults.Ok(await extraction.FailedAsync(cancellationToken));

    private static Accepted<ExtractionStatusDto> CancelRun(ExtractionCoordinator coordinator, VacancyExtractionService extraction)
    {
        coordinator.Cancel();
        return TypedResults.Accepted("/api/extractions", coordinator.Status(extraction.IsAvailable, extraction.Model));
    }

    /// <summary>„Mit KI analysieren": this vacancy next, ahead of a running backfill.</summary>
    /// <param name="retry">„Erneut" from the list of failed ones: forget the failed attempt first.</param>
    private static async Task<Results<Accepted<ExtractionStatusDto>, NotFound, ProblemHttpResult>> ExtractVacancy(
        Guid id, AppDbContext db, ExtractionCoordinator coordinator, VacancyExtractionService extraction, bool? retry, CancellationToken cancellationToken)
    {
        var description = await db.Vacancies.AsNoTracking().Where(v => v.Id == id).Select(v => new { v.Description }).SingleOrDefaultAsync(cancellationToken);
        if (description is null)
        {
            return TypedResults.NotFound();
        }

        if (!extraction.IsAvailable)
        {
            return NotAvailable();
        }

        if (string.IsNullOrWhiteSpace(description.Description))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Keine Beschreibung.",
                detail: "Ohne Beschreibung gibt es nichts zu analysieren – erst „Beschreibung einfügen\".");
        }

        if (retry == true)
        {
            await extraction.ForgetFailureAsync(id, cancellationToken);
        }

        coordinator.RequestVacancy(id);
        return TypedResults.Accepted("/api/extractions", coordinator.Status(true, extraction.Model));
    }

    private static ProblemHttpResult NotAvailable() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "KI-Analyse nicht eingerichtet.",
        detail: "Llm:Tasks:Extraction und den Anbieter einrichten (Ollama: Llm:Providers:Ollama:Enabled = true). Bis dahin gelten die Regeln.");

    private static async Task<Results<Ok<GoldLabelDto>, NotFound, ValidationProblem>> SetLabel(
        Guid id, GoldLabelRequest request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (request.Fits is not { } fits)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["fits"] = ["true (Passt) oder false (Passt nicht)."] });
        }

        if (!await db.Vacancies.AnyAsync(v => v.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var label = await db.GoldLabels.SingleOrDefaultAsync(l => l.VacancyId == id, cancellationToken);
        if (label is null)
        {
            label = new GoldLabel { VacancyId = id };
            db.GoldLabels.Add(label);
        }

        label.Fits = fits;
        label.LabeledAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new GoldLabelDto(id, label.Fits, label.LabeledAt));
    }

    private static async Task<NoContent> DeleteLabel(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        await db.GoldLabels.Where(l => l.VacancyId == id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<GoldLabelSummaryDto>> GetLabelSummary(AppDbContext db, CancellationToken cancellationToken)
    {
        var counts = await db.GoldLabels.AsNoTracking().GroupBy(l => l.Fits).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var fits = counts.Where(c => c.Key).Sum(c => c.Count);
        var notFits = counts.Where(c => !c.Key).Sum(c => c.Count);
        return TypedResults.Ok(new GoldLabelSummaryDto(fits + notFits, fits, notFits, LabelTarget));
    }
}

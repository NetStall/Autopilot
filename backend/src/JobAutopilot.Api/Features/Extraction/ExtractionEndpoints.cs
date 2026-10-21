using JobAutopilot.Core.Extraction;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Extraction;

/// <summary>Body of <c>PUT /api/vacancies/{id}/label</c>.</summary>
public sealed record GoldLabelRequest(bool? Fits);

public sealed record GoldLabelDto(Guid VacancyId, bool Fits, DateTimeOffset LabeledAt);

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

        app.MapPost("/api/vacancies/{id:guid}/extract", ExtractVacancy).WithTags("Extraction").WithName(nameof(ExtractVacancy));

        app.MapPut("/api/vacancies/{id:guid}/label", SetLabel).WithTags("Gold labels").WithName(nameof(SetLabel));
        app.MapDelete("/api/vacancies/{id:guid}/label", DeleteLabel).WithTags("Gold labels").WithName(nameof(DeleteLabel));
        app.MapGet("/api/labels/summary", GetLabelSummary).WithTags("Gold labels").WithName(nameof(GetLabelSummary));
        return app;
    }

    private static Ok<ExtractionStatusDto> GetStatus(ExtractionCoordinator coordinator, VacancyExtractionService extraction) =>
        TypedResults.Ok(coordinator.Status(extraction.IsAvailable, extraction.Model));

    /// <summary>„Alle analysieren": every vacancy with a description that has no current extraction.</summary>
    private static Results<Accepted<ExtractionStatusDto>, ProblemHttpResult> StartBackfill(
        ExtractionCoordinator coordinator, VacancyExtractionService extraction)
    {
        if (!extraction.IsAvailable)
        {
            return NotAvailable();
        }

        coordinator.RequestBackfill();
        return TypedResults.Accepted("/api/extractions", coordinator.Status(true, extraction.Model));
    }

    private static Accepted<ExtractionStatusDto> CancelRun(ExtractionCoordinator coordinator, VacancyExtractionService extraction)
    {
        coordinator.Cancel();
        return TypedResults.Accepted("/api/extractions", coordinator.Status(extraction.IsAvailable, extraction.Model));
    }

    /// <summary>„Mit KI analysieren": this vacancy next, ahead of a running backfill.</summary>
    private static async Task<Results<Accepted<ExtractionStatusDto>, NotFound, ProblemHttpResult>> ExtractVacancy(
        Guid id, AppDbContext db, ExtractionCoordinator coordinator, VacancyExtractionService extraction, CancellationToken cancellationToken)
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

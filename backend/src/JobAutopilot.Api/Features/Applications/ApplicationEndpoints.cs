using System.ComponentModel.DataAnnotations;
using JobAutopilot.Core.Applications;
using JobAutopilot.Infrastructure.Applications;
using JobAutopilot.Infrastructure.Documents;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Applications;

/// <summary>
/// Applications (personal data, localhost only). Recording only — nothing is ever sent from here (AGENTS.md).
/// </summary>
public static partial class ApplicationEndpoints
{
    private const string LogCategory = "JobAutopilot.Api.Applications";

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app, string trackerImportPath)
    {
        var group = app.MapGroup("/api/applications").WithTags("Applications");
        group.MapGet("/", List).WithName("ListApplications");
        group.MapGet("/{id:guid}", Get).WithName("GetApplication");
        group.MapPost("/", Create).WithName("CreateApplication");
        group.MapPatch("/{id:guid}", Update).WithName("UpdateApplication");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteApplication");
        group.MapPost("/{id:guid}/status", ChangeStatus).WithName("ChangeApplicationStatus");
        group.MapPost("/import-tracker", (TrackerImporter importer, ILoggerFactory loggers, CancellationToken ct) =>
                ImportTracker(importer, trackerImportPath, loggers, ct))
            .WithName("ImportTracker");

        app.MapPost("/api/vacancies/{id:guid}/apply", MarkApplied).WithTags("Applications").WithName("MarkVacancyApplied");
        return app;
    }

    public sealed class ListQuery
    {
        [FromQuery(Name = "status")]
        [MaxLength(11)]
        public ApplicationStatus[]? Status { get; init; }

        [FromQuery(Name = "channel")]
        [MaxLength(100)]
        public string? Channel { get; init; }

        [FromQuery(Name = "followUpDue")]
        public bool? FollowUpDue { get; init; }
    }

    /// <summary>Newest first; filters by status (repeatable), channel and „Follow-up fällig".</summary>
    private static async Task<Ok<List<ApplicationDto>>> List(
        [AsParameters] ListQuery query,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var applications = db.Applications.AsNoTracking();
        if (query.Status is { Length: > 0 } statuses)
        {
            applications = applications.Where(a => statuses.Contains(a.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            var channel = query.Channel.Trim();
            applications = applications.Where(a => a.Channel == channel);
        }

        var now = timeProvider.GetUtcNow();
        var list = await applications.OrderByDescending(a => a.AppliedAt).ThenByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);
        var result = list
            .Where(a => query.FollowUpDue != true || a.IsFollowUpDue(now))
            .Select(a => ApplicationDto.From(a, now))
            .ToList();
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ApplicationDto>, NotFound>> Get(
        Guid id, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var application = await db.Applications.AsNoTracking().Include(a => a.Events)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return application is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ApplicationDto.From(application, timeProvider.GetUtcNow(), withEvents: true));
    }

    /// <summary>A manual application (status Sent: it has been sent outside the tool).</summary>
    private static async Task<Results<Created<ApplicationDto>, ProblemHttpResult>> Create(
        ApplicationRequest request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (request.VacancyId is { } vacancyId && !await db.Vacancies.AnyAsync(v => v.Id == vacancyId, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown vacancy.");
        }

        var now = timeProvider.GetUtcNow();
        var applied = request.AppliedAt ?? now;
        var application = new Application
        {
            Id = Guid.CreateVersion7(now),
            Company = request.Company,
            Position = request.Position,
            VacancyId = request.VacancyId,
            AppliedAt = applied,
            LastActionAt = applied,
            CreatedAt = now,
            UpdatedAt = now,
        };
        request.ApplyTo(application);
        application.ChangeStatus(ApplicationStatus.Sent, applied, "Angelegt");
        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/applications/{application.Id}", ApplicationDto.From(application, now, withEvents: true));
    }

    private static async Task<Results<Ok<ApplicationDto>, NotFound>> Update(
        Guid id, ApplicationRequest request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var application = await db.Applications.Include(a => a.Events).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        request.ApplyTo(application);
        if (request.AppliedAt is { } applied)
        {
            application.AppliedAt = applied;
        }

        var now = timeProvider.GetUtcNow();
        application.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ApplicationDto.From(application, now, withEvents: true));
    }

    /// <summary>Deletes the application, its history, its documents (cascade) and the approved PDFs on disk.</summary>
    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, AppDbContext db, DocumentFileStore files, CancellationToken cancellationToken)
    {
        var deleted = await db.Applications.Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            files.Delete(id);
        }

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// Writes a history event and makes it the last action (resets the follow-up date). Entwurf, Freigegeben and
    /// Gesendet cannot be set here — approving and „Als gesendet markieren" are explicit actions (step 06).
    /// </summary>
    private static async Task<Results<Ok<ApplicationDto>, NotFound, ProblemHttpResult>> ChangeStatus(
        Guid id, StatusChangeRequest request, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var application = await db.Applications.Include(a => a.Events).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Status!.Value != application.Status && !ApplicationWorkflow.AllowsManualChange(application.Status, request.Status.Value))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Statuswechsel nicht erlaubt.",
                detail: "„Entwurf“, „Freigegeben“ und „Gesendet“ entstehen nur über „Unterlagen erstellen“, „Freigeben“ und „Als gesendet markieren“.");
        }

        var now = timeProvider.GetUtcNow();
        var before = application.Events.Count;
        if (!application.ChangeStatus(request.Status!.Value, request.At ?? now, request.Note)
            && !string.IsNullOrWhiteSpace(request.Note))
        {
            // Same status, but a note (e.g. „Follow-up gesendet") is still an action worth recording.
            application.Events.Add(new ApplicationEvent
            {
                Id = Guid.CreateVersion7(now), ApplicationId = application.Id, At = request.At ?? now,
                From = application.Status, To = application.Status, Note = request.Note.Trim(),
            });
            application.LastActionAt = request.At ?? now;
        }

        foreach (var added in application.Events.Skip(before))
        {
            db.ApplicationEvents.Add(added);
        }

        application.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ApplicationDto.From(application, now, withEvents: true));
    }

    /// <summary>„Als beworben markieren": an application (Sent) pre-filled from the vacancy; the existing one if there is one.</summary>
    private static async Task<Results<Created<ApplicationDto>, Ok<ApplicationDto>, NotFound>> MarkApplied(
        Guid id, AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vacancy is null)
        {
            return TypedResults.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var existing = await db.Applications.AsNoTracking().Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.VacancyId == id, cancellationToken);
        if (existing is not null)
        {
            return TypedResults.Ok(ApplicationDto.From(existing, now, withEvents: true));
        }

        var application = Application.ForVacancy(vacancy, now);
        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/applications/{application.Id}", ApplicationDto.From(application, now, withEvents: true));
    }

    private static async Task<Results<Ok<TrackerImportReport>, ProblemHttpResult>> ImportTracker(
        TrackerImporter importer, string path, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await importer.ImportFileAsync(path, cancellationToken));
        }
        catch (TrackerImportException ex)
        {
            LogImportFailed(loggers.CreateLogger(LogCategory));
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tracker import failed.", detail: ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tracker import failed (file missing or invalid)")]
    private static partial void LogImportFailed(ILogger logger);
}

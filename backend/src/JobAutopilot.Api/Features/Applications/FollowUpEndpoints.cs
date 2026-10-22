using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Profile;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Applications;

/// <summary>One due follow-up with the text to copy (e-mail or LinkedIn); I send it myself.</summary>
public sealed record FollowUpDto(
    Guid ApplicationId, string Company, string Position, ApplicationStatus Status, string StatusLabel, DateTimeOffset AppliedAt,
    DateTimeOffset LastActionAt, DateTimeOffset DueAt, FollowUpDraft Draft);

public static class FollowUpEndpoints
{
    public static IEndpointRouteBuilder MapFollowUpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/follow-ups", Due).WithTags("Applications").WithName("DueFollowUps");
        app.MapPost("/api/applications/{id:guid}/follow-up-done", Done).WithTags("Applications").WithName("FollowUpDone");
        return app;
    }

    /// <summary>„Follow-ups fällig": open applications (Gesendet, Eingangsbestätigung, Rückmeldung) 7 days after the last action, oldest first.</summary>
    private static async Task<Ok<List<FollowUpDto>>> Due(AppDbContext db, ICandidateProfileSource profiles, TimeProvider timeProvider, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var open = await db.Applications.AsNoTracking()
            .Where(a => a.Status == ApplicationStatus.Sent || a.Status == ApplicationStatus.Acknowledged || a.Status == ApplicationStatus.Response)
            .ToListAsync(ct);
        var due = open.Where(a => a.IsFollowUpDue(now)).ToList();
        var ids = due.Select(a => a.Id).ToList();
        // The letter's language and form of address, when the documents were made here.
        var tones = await db.ApplicationDrafts.AsNoTracking().Where(d => ids.Contains(d.ApplicationId))
            .Select(d => new { d.ApplicationId, d.Letter }).ToDictionaryAsync(d => d.ApplicationId, d => (d.Letter.Language, d.Letter.Formality), ct);
        var sender = profiles.Current.Usable?.Personal.Name ?? string.Empty;
        return TypedResults.Ok(due
            .OrderBy(a => a.FollowUpAt)
            .Select(a =>
            {
                var (language, formality) = tones.TryGetValue(a.Id, out var tone) ? tone : (DocumentLanguage.De, Formality.Sie);
                return new FollowUpDto(a.Id, a.Company, a.Position, a.Status, ApplicationStatuses.Label(a.Status), a.AppliedAt, a.LastActionAt, a.FollowUpAt!.Value,
                    FollowUps.Draft(a, language, formality, sender));
            })
            .ToList());
    }

    /// <summary>„Erledigt": the last action is now; the next follow-up is due in 7 days.</summary>
    private static async Task<Results<NoContent, NotFound>> Done(Guid id, AppDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        var application = await db.Applications.Include(a => a.Events).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var count = application.Events.Count;
        FollowUps.MarkDone(application, timeProvider.GetUtcNow());
        db.ApplicationEvents.AddRange(application.Events.Skip(count));
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

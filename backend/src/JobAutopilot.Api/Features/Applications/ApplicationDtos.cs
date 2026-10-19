using System.ComponentModel.DataAnnotations;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Applications;

public sealed record ApplicationDto(
    Guid Id,
    Guid? VacancyId,
    string Company,
    string Position,
    string? Location,
    string? Channel,
    string? ContactName,
    string? ContactEmail,
    string? Url,
    DateTimeOffset AppliedAt,
    DateTimeOffset LastActionAt,
    ApplicationStatus Status,
    string StatusLabel,
    bool IsOpen,
    DateTimeOffset? FollowUpAt,
    bool IsFollowUpDue,
    int? SalaryExpectation,
    IReadOnlyList<string> Documents,
    string? NextStep,
    string? Notes,
    string? ExternalRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ApplicationEventDto>? Events)
{
    public static ApplicationDto From(Application a, DateTimeOffset now, bool withEvents = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new(
            a.Id, a.VacancyId, a.Company, a.Position, a.Location, a.Channel, a.ContactName, a.ContactEmail, a.Url,
            a.AppliedAt, a.LastActionAt, a.Status, ApplicationStatuses.Label(a.Status), Application.IsOpen(a.Status),
            a.FollowUpAt, a.IsFollowUpDue(now), a.SalaryExpectation, [.. a.Documents], a.NextStep, a.Notes, a.ExternalRef,
            a.CreatedAt, a.UpdatedAt,
            withEvents ? [.. a.Events.OrderByDescending(e => e.At).Select(ApplicationEventDto.Of)] : null);
    }
}

public sealed record ApplicationEventDto(DateTimeOffset At, ApplicationStatus? From, ApplicationStatus To, string ToLabel, string? Note)
{
    public static ApplicationEventDto Of(ApplicationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new(e.At, e.From, e.To, ApplicationStatuses.Label(e.To), e.Note);
    }
}

/// <summary>Body of <c>POST /api/applications</c> and <c>PATCH /api/applications/{id}</c> (status has its own endpoint).</summary>
public sealed class ApplicationRequest
{
    [Required]
    [MaxLength(VacancyLimits.CompanyMaxLength)]
    public string Company { get; init; } = string.Empty;

    [Required]
    [MaxLength(VacancyLimits.TitleMaxLength)]
    public string Position { get; init; } = string.Empty;

    [MaxLength(VacancyLimits.LocationMaxLength)]
    public string? Location { get; init; }

    [MaxLength(100)]
    public string? Channel { get; init; }

    [MaxLength(200)]
    public string? ContactName { get; init; }

    [EmailAddress]
    [MaxLength(320)]
    public string? ContactEmail { get; init; }

    [Url]
    [MaxLength(VacancyLimits.UrlMaxLength)]
    public string? Url { get; init; }

    public DateTimeOffset? AppliedAt { get; init; }

    [Range(0, 1_000_000)]
    public int? SalaryExpectation { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? Documents { get; init; }

    [MaxLength(2000)]
    public string? NextStep { get; init; }

    [MaxLength(10_000)]
    public string? Notes { get; init; }

    public Guid? VacancyId { get; init; }

    public void ApplyTo(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Company = Company.Trim();
        application.Position = Position.Trim();
        application.Location = Blank(Location);
        application.Channel = Blank(Channel);
        application.ContactName = Blank(ContactName);
        application.ContactEmail = Blank(ContactEmail);
        application.Url = Blank(Url);
        application.SalaryExpectation = SalaryExpectation;
        application.Documents = [.. (Documents ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())];
        application.NextStep = Blank(NextStep);
        application.Notes = Blank(Notes);
        application.RefreshDuplicateKey();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Body of <c>POST /api/applications/{id}/status</c>.</summary>
public sealed class StatusChangeRequest
{
    [Required]
    public ApplicationStatus? Status { get; init; }

    [MaxLength(2000)]
    public string? Note { get; init; }

    /// <summary>When it happened; default now.</summary>
    public DateTimeOffset? At { get; init; }
}

using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Applications;

/// <summary>Status of an application (AGENTS.md); German UI labels in <see cref="ApplicationStatuses"/>.</summary>
public enum ApplicationStatus
{
    Draft,
    Approved,
    Sent,
    Acknowledged,
    Response,
    Interview,
    TechInterview,
    Offer,
    Rejected,
    NoResponse,
    Withdrawn,
}

/// <summary>
/// Vitalii's application to one job — personal data. Applications in this step are recorded by hand or imported from
/// his tracker; nothing is ever sent by the tool (AGENTS.md).
/// </summary>
public class Application
{
    public const int FollowUpDays = 7;

    public Guid Id { get; set; }

    /// <summary>The stored vacancy, if known. Cleared when the vacancy is deleted (e.g. an expired Himalayas job).</summary>
    public Guid? VacancyId { get; set; }

    public required string Company { get; set; }

    public required string Position { get; set; }

    public string? Location { get; set; }

    /// <summary>Where the job was found / how the application was made, e.g. „LinkedIn", „Firmenwebsite".</summary>
    public string? Channel { get; set; }

    public string? ContactName { get; set; }

    public string? ContactEmail { get; set; }

    public string? Url { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public DateTimeOffset LastActionAt { get; set; }

    public ApplicationStatus Status { get; set; }

    /// <summary>Yearly salary in EUR that was stated (an hourly wage stays in the notes).</summary>
    public int? SalaryExpectation { get; set; }

    /// <summary>File names of the documents sent (CV, cover letter …) — names only, never the files.</summary>
    public List<string> Documents { get; set; } = [];

    public string? NextStep { get; set; }

    public string? Notes { get; set; }

    /// <summary>Id in the imported tracker; makes the import idempotent.</summary>
    public string? ExternalRef { get; set; }

    /// <summary>Company + position normalised like <see cref="Vacancy.DuplicateKey"/> — for the „Beworben" badge.</summary>
    public string? DuplicateKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ApplicationEvent> Events { get; set; } = [];

    public static bool IsOpen(ApplicationStatus status) =>
        status is ApplicationStatus.Sent or ApplicationStatus.Acknowledged or ApplicationStatus.Response;

    /// <summary>Last action + 7 days while the application is open (Sent, Acknowledged, Response); otherwise none.</summary>
    public DateTimeOffset? FollowUpAt => IsOpen(Status) ? LastActionAt.AddDays(FollowUpDays) : null;

    /// <summary>The follow-up date has been reached (comparing calendar days in Germany).</summary>
    public bool IsFollowUpDue(DateTimeOffset now) =>
        FollowUpAt is { } due
        && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(due, TimeZones.Berlin).DateTime)
            <= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZones.Berlin).DateTime);

    /// <summary>Records a status change in the history and makes it the last action. The same status again is a no-op.</summary>
    public bool ChangeStatus(ApplicationStatus to, DateTimeOffset at, string? note = null)
    {
        if (!Enum.IsDefined(to))
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "Unknown status.");
        }

        if (to == Status && Events.Count > 0)
        {
            return false;
        }

        Events.Add(new ApplicationEvent
        {
            Id = Guid.CreateVersion7(at),
            ApplicationId = Id,
            At = at,
            From = Events.Count == 0 ? null : Status,
            To = to,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        });
        Status = to;
        if (at > LastActionAt)
        {
            LastActionAt = at;
        }

        UpdatedAt = at > UpdatedAt ? at : UpdatedAt;
        return true;
    }

    public void RefreshDuplicateKey() => DuplicateKey = DuplicateKeys.For(Company, Position);

    /// <summary>A new application for a stored vacancy („Als beworben markieren"), status <see cref="ApplicationStatus.Sent"/>.</summary>
    public static Application ForVacancy(Vacancy vacancy, DateTimeOffset now, string? channel = null)
    {
        ArgumentNullException.ThrowIfNull(vacancy);
        var application = new Application
        {
            Id = Guid.CreateVersion7(now),
            VacancyId = vacancy.Id,
            Company = vacancy.Company,
            Position = vacancy.Title,
            Location = vacancy.Location,
            Url = vacancy.Url,
            Channel = channel ?? vacancy.Source switch
            {
                VacancySource.Arbeitsagentur => "Arbeitsagentur",
                VacancySource.Himalayas => "Himalayas",
                _ => null,
            },
            AppliedAt = now,
            LastActionAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        application.RefreshDuplicateKey();
        application.ChangeStatus(ApplicationStatus.Sent, now, "Als beworben markiert");
        return application;
    }
}

/// <summary>One status change in the history of an application.</summary>
public class ApplicationEvent
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary><c>null</c> for the first event (created / imported).</summary>
    public ApplicationStatus? From { get; set; }

    public ApplicationStatus To { get; set; }

    public string? Note { get; set; }
}

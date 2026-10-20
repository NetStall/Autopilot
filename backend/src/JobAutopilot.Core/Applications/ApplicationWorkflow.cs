using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Applications;

/// <summary>Why a workflow step was refused (German message, safe to show).</summary>
public sealed class WorkflowException : InvalidOperationException
{
    public WorkflowException()
        : base("Dieser Schritt ist im aktuellen Status nicht möglich.")
    {
    }

    public WorkflowException(string message)
        : base(message)
    {
    }

    public WorkflowException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Draft → Approved → Sent for applications prepared in the tool (step 06). The tool never sends anything: „Sent"
/// only records that I submitted the approved documents myself, and only via <see cref="MarkSent"/> after
/// <see cref="Approve"/> (AGENTS.md: nothing leaves without an explicit approval of exactly that application).
/// </summary>
public static class ApplicationWorkflow
{
    /// <summary>A new application in status Draft for a vacancy („Unterlagen erstellen").</summary>
    public static Application NewDraft(Vacancy vacancy, DateTimeOffset now)
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
            AppliedAt = now,
            LastActionAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        application.RefreshDuplicateKey();
        application.ChangeStatus(ApplicationStatus.Draft, now, "Unterlagen erstellt");
        return application;
    }

    /// <summary>Documents can be (re)created while the application is not yet sent.</summary>
    public static bool CanPrepare(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.Status is ApplicationStatus.Draft or ApplicationStatus.Approved;
    }

    /// <summary>
    /// The generic status change (tracker, „Absage", „Interview" …) cannot enter or leave the preparation states —
    /// approving and sending have their own explicit actions. Withdrawing a draft is allowed.
    /// </summary>
    public static bool AllowsManualChange(ApplicationStatus from, ApplicationStatus to) =>
        to is not (ApplicationStatus.Draft or ApplicationStatus.Approved or ApplicationStatus.Sent)
        && (from is not (ApplicationStatus.Draft or ApplicationStatus.Approved) || to == ApplicationStatus.Withdrawn);

    /// <summary>„Freigeben". With open warnings only when <paramref name="acceptWarnings"/> confirms them.</summary>
    public static void Approve(Application application, ApplicationDraft draft, DateTimeOffset now, bool acceptWarnings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(draft);
        if (!CanPrepare(application))
        {
            throw new WorkflowException("Nur Entwürfe können freigegeben werden.");
        }

        if (draft.HasOpenWarnings && !acceptWarnings)
        {
            throw new WorkflowException("Es gibt offene Warnungen. Zum Freigeben bitte ausdrücklich bestätigen.");
        }

        draft.ApprovedAt = now;
        draft.WarningsAccepted = draft.HasOpenWarnings;
        application.ChangeStatus(ApplicationStatus.Approved, now,
            draft.WarningsAccepted ? "Unterlagen freigegeben (mit bestätigten Warnungen)" : "Unterlagen freigegeben");
        application.Documents = [.. new[] { draft.LetterFile, draft.CvFile }.OfType<string>()];
    }

    /// <summary>A change after approval: back to Draft, the approval must be given again.</summary>
    public static void Reopen(Application application, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.Status == ApplicationStatus.Approved)
        {
            application.ChangeStatus(ApplicationStatus.Draft, now, "Nach der Freigabe geändert");
        }
    }

    /// <summary>„Als gesendet markieren": I submitted the approved documents myself via <paramref name="channel"/>.</summary>
    public static void MarkSent(Application application, ApplicationDraft? draft, string channel, DateTimeOffset at, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        if (application.Status != ApplicationStatus.Approved || draft?.ApprovedAt is null)
        {
            throw new WorkflowException("Erst freigeben, dann als gesendet markieren.");
        }

        application.Channel = channel.Trim();
        application.AppliedAt = at;
        application.ChangeStatus(ApplicationStatus.Sent, at, $"Gesendet über {application.Channel}");
        application.LastActionAt = at > application.LastActionAt ? at : application.LastActionAt;
        application.UpdatedAt = now;
    }
}

using System.Globalization;
using JobAutopilot.Core.Documents;

namespace JobAutopilot.Core.Applications;

/// <summary>A follow-up text to copy: e-mail (subject + body) and a short LinkedIn message. Only text — never a phone call.</summary>
public sealed record FollowUpDraft(string Subject, string Email, string LinkedIn);

/// <summary>
/// „Follow-ups fällig" (08c): open applications whose last action is 7 days ago get a polite 3–4-sentence nudge in the
/// application's language and form of address, naming the position and the date I applied. Deterministic — no model,
/// no invented contact („as discussed on the phone" never appears). I send it myself.
/// </summary>
public static class FollowUps
{
    public static FollowUpDraft Draft(Application application, DocumentLanguage language, Formality formality, string senderName)
    {
        ArgumentNullException.ThrowIfNull(application);
        var position = application.Position.Trim();
        var company = application.Company.Trim();
        var contact = string.IsNullOrWhiteSpace(application.ContactName) ? null : application.ContactName.Trim();
        if (language == DocumentLanguage.En)
        {
            var date = application.AppliedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            var hello = contact is null ? $"Dear {company} team," : $"Dear {contact},";
            return new FollowUpDraft(
                $"Follow-up: my application as {position}",
                $"{hello}\n\non {date} I applied for the position of {position}. I would like to kindly ask whether my application has "
                + "arrived and how the process is going. I am still very interested in the role and happy to provide any further "
                + $"information.\n\nKind regards\n{senderName}",
                $"Hello{(contact is null ? string.Empty : " " + contact)}, on {date} I applied for the {position} position at {company}. "
                + "Is there any news on my application? I am still very interested.");
        }

        var datum = application.AppliedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        if (formality == Formality.Du)
        {
            var hallo = contact is null ? $"Hallo liebes {CompanyNames.ShortName(company) ?? company}-Team," : $"Hallo {contact},";
            return new FollowUpDraft(
                $"Nachfrage zu meiner Bewerbung als {position}",
                $"{hallo}\n\nam {datum} habe ich mich bei euch als {position} beworben. Ich wollte kurz nachfragen, ob meine Unterlagen "
                + "angekommen sind und wie der aktuelle Stand ist. Die Stelle interessiert mich weiterhin sehr, und ich freue mich über eine "
                + $"kurze Rückmeldung.\n\nViele Grüße\n{senderName}",
                $"Hallo{(contact is null ? string.Empty : " " + contact)}, am {datum} habe ich mich bei {company} als {position} beworben. "
                + "Gibt es schon einen Stand zu meiner Bewerbung? Die Stelle interessiert mich weiterhin sehr.");
        }

        var anrede = contact is null ? "Sehr geehrte Damen und Herren," : $"Guten Tag {contact},";
        return new FollowUpDraft(
            $"Nachfrage zu meiner Bewerbung als {position}",
            $"{anrede}\n\nam {datum} habe ich mich bei Ihnen als {position} beworben. Ich möchte freundlich nachfragen, ob meine "
            + "Unterlagen angekommen sind und wie der aktuelle Stand des Verfahrens ist. Die Stelle interessiert mich weiterhin sehr, "
            + $"und ich freue mich über eine kurze Rückmeldung.\n\nMit freundlichen Grüßen\n{senderName}",
            $"Guten Tag{(contact is null ? string.Empty : " " + contact)}, am {datum} habe ich mich bei {company} als {position} beworben. "
            + "Gibt es bereits einen Stand zu meiner Bewerbung? Die Stelle interessiert mich weiterhin sehr.");
    }

    /// <summary>„Erledigt": the follow-up is a new last action (the status stays); the next one is due in 7 days.</summary>
    public static void MarkDone(Application application, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Events.Add(new ApplicationEvent
        {
            Id = Guid.CreateVersion7(at),
            ApplicationId = application.Id,
            At = at,
            From = application.Status,
            To = application.Status,
            Note = "Nachgefasst (E-Mail / LinkedIn)",
        });
        application.LastActionAt = at > application.LastActionAt ? at : application.LastActionAt;
        application.UpdatedAt = at > application.UpdatedAt ? at : application.UpdatedAt;
    }
}

using JobAutopilot.Core.Profile;

namespace JobAutopilot.Core.Documents;

public enum RenderFormat
{
    Pdf,

    /// <summary>PNG of the first page — the live preview in the review screen.</summary>
    Png,
}

/// <summary>A rendered document and how many pages it has.</summary>
public sealed record RenderedDocument(byte[] Content, int Pages);

/// <summary>The letter as printed: my header, recipient block, place and date, the letter, my name.</summary>
public sealed record LetterPrint(
    string SenderName,
    string RoleLine,
    IReadOnlyList<CvContact> SenderContacts,
    IReadOnlyList<string> RecipientLines,
    string PlaceAndDate,
    LetterDraft Letter)
{
    /// <summary>Builds the print layout; the role line is the CV's, so letter and CV look like one set.</summary>
    public static LetterPrint Create(CandidateProfile profile, LetterDraft letter, LetterVacancy vacancy, ToneResult tone, string roleLine, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(letter);
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(tone);

        var personal = profile.Personal;
        var contacts = new List<CvContact> { new(personal.Email, $"mailto:{personal.Email}") };
        if (!string.IsNullOrWhiteSpace(personal.Phone))
        {
            contacts.Add(new(personal.Phone, null));
        }

        if (!string.IsNullOrWhiteSpace(personal.City))
        {
            contacts.Add(new(personal.City, null));
        }

        foreach (var url in new[] { personal.Website, personal.Github })
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                contacts.Add(new(Links.Display(url), url));
            }
        }

        var recipient = new List<string> { vacancy.Company };
        if (tone.Contact is { } contact)
        {
            recipient.Add(letter.Language == DocumentLanguage.De ? contact.Display : contact.Display.Replace("Frau", "Ms", StringComparison.Ordinal).Replace("Herr", "Mr", StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(vacancy.Location))
        {
            recipient.Add(vacancy.Location);
        }

        var place = string.IsNullOrWhiteSpace(personal.City) ? "" : personal.City + ", ";
        return new LetterPrint(personal.Name, roleLine, contacts, recipient, place + Formats.LongDate(today, letter.Language), letter);
    }
}

/// <summary>Renders letter and CV (QuestPDF in Infrastructure). Pure function of its input.</summary>
public interface IDocumentRenderer
{
    RenderedDocument RenderLetter(LetterPrint letter, RenderFormat format);

    RenderedDocument RenderCv(CvDocument cv, RenderFormat format);
}

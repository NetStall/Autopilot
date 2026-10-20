using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Documents;

/// <summary>One value to copy into an application form.</summary>
public sealed record CheatSheetItem(string Key, string Label, string Value, bool Multiline = false);

/// <summary>
/// „Spickzettel" for application forms and e-mail applications: subject, a short e-mail text, salary expectation,
/// earliest start, how I found the job, German level, links and contact person — only from the profile, the
/// Suchprofil, the vacancy and the letter. Labels are German (the forms are), values follow the letter's language.
/// </summary>
public static class CheatSheet
{
    public static IReadOnlyList<CheatSheetItem> Build(
        CandidateProfile profile,
        LetterDraft letter,
        LetterVacancy vacancy,
        VacancySource source,
        ToneResult tone,
        SearchProfile? searchProfile,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(letter);
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(tone);
        var language = letter.Language;
        var items = new List<CheatSheetItem>
        {
            new("subject", "Betreff", letter.Subject),
            new("email", "E-Mail-Text", EmailText(profile, letter, vacancy, tone, searchProfile, today), Multiline: true),
            new("name", "Name", profile.Personal.Name),
            new("emailAddress", "E-Mail", profile.Personal.Email),
        };

        Add(items, "phone", "Telefon", profile.Personal.Phone);
        Add(items, "city", "Wohnort", profile.Personal.City);
        Add(items, "salary", "Gehaltsvorstellung", Formats.SalaryRange(searchProfile?.SalaryMin, searchProfile?.SalaryMax, language));
        Add(items, "start", "Frühester Eintritt", searchProfile?.EarliestStart is { } start ? Formats.Date(start, language) : null);
        Add(items, "foundVia", "Wie haben Sie uns gefunden?", FoundVia(source, language));
        Add(items, "german", "Deutschkenntnisse", GermanLevel(profile));
        Add(items, "website", "Website", profile.Personal.Website);
        Add(items, "github", "GitHub", profile.Personal.Github);
        Add(items, "linkedin", "LinkedIn", profile.Personal.Linkedin);
        Add(items, "contact", "Ansprechperson", tone.Contact?.Display);
        return items;
    }

    /// <summary>3–4 sentences for an e-mail application with the documents attached.</summary>
    public static string EmailText(
        CandidateProfile profile, LetterDraft letter, LetterVacancy vacancy, ToneResult tone, SearchProfile? searchProfile, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(letter);
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(tone);

        var years = ProfileValidator.ExperienceMonths(profile, today) / 12;
        var terms = new VacancyTerms(profile, vacancy.Title, vacancy.Description);
        var focus = CvBuilder.DetectFocus(vacancy.Title, vacancy.Description, [.. profile.Summary.Keys.Where(k => !k.EndsWith(".de", StringComparison.OrdinalIgnoreCase))]);
        var skills = CvBuilder.SkillBlocks(profile, terms, focus).Select(b => b.Replace(" / ", "/", StringComparison.Ordinal)).ToList();
        var start = searchProfile?.EarliestStart is { } earliest ? Formats.Date(earliest, letter.Language) : null;
        var website = profile.Personal.Website is { } url ? Links.Display(url) : null;
        var salutation = string.IsNullOrWhiteSpace(letter.Salutation) ? tone.Salutation : letter.Salutation;

        if (letter.Language == DocumentLanguage.En)
        {
            var english = new List<string>
            {
                $"Please find attached my application for the position of {vacancy.Title} (cover letter and CV).",
                skills.Count > 0
                    ? $"I have {years} years of professional experience, mainly with {Join(skills, "and")}."
                    : $"I have {years} years of professional experience.",
            };
            if (start is not null)
            {
                english.Add($"I can start on {start}.");
            }
            else if (website is not null)
            {
                english.Add($"More about my work: {website}.");
            }

            english.Add("I look forward to hearing from you.");
            return $"{salutation}\n\n{string.Join("\n", english)}\n\nBest regards\n{profile.Personal.Name}";
        }

        var du = letter.Formality == Formality.Du;
        var you = du ? (tone.Contact is null ? "euch" : "dir") : "Ihnen";
        var body = new List<string>
        {
            $"anbei {(du ? "schicke" : "sende")} ich {you} meine Bewerbung als {vacancy.Title} – Anschreiben und Lebenslauf im Anhang.",
            skills.Count > 0
                ? $"Ich habe {years} Jahre Berufserfahrung, vor allem mit {Join(skills, "und")}."
                : $"Ich habe {years} Jahre Berufserfahrung.",
        };
        if (start is not null)
        {
            body.Add($"Ich kann ab {start} beginnen.");
        }
        else if (website is not null)
        {
            body.Add($"Mehr zu meiner Arbeit: {website}.");
        }

        body.Add(du ? "Ich freue mich auf eure Rückmeldung." : "Über eine Rückmeldung freue ich mich.");
        if (du && tone.Contact is not null)
        {
            body[^1] = "Ich freue mich auf deine Rückmeldung.";
        }

        return $"{salutation}\n\n{string.Join("\n", body)}\n\n{(du ? "Viele Grüße" : "Mit freundlichen Grüßen")}\n{profile.Personal.Name}";
    }

    public static string? FoundVia(VacancySource source, DocumentLanguage language) => source switch
    {
        VacancySource.Arbeitsagentur => language == DocumentLanguage.De
            ? "Jobbörse der Bundesagentur für Arbeit"
            : "Job board of the Bundesagentur für Arbeit (Federal Employment Agency)",
        VacancySource.Himalayas => language == DocumentLanguage.De ? "Himalayas (Jobbörse für Remote-Stellen)" : "Himalayas (remote job board)",
        _ => null,
    };

    /// <summary>„B1 (DTZ, 10/2026)" from the profile's German entry.</summary>
    public static string? GermanLevel(CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var german = profile.Languages.FirstOrDefault(l =>
            l.Name.Equals("Deutsch", StringComparison.OrdinalIgnoreCase) || l.Name.Equals("German", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.NameDe, "Deutsch", StringComparison.OrdinalIgnoreCase));
        if (german is null)
        {
            return null;
        }

        var note = german.NoteDe ?? german.Note;
        var level = german.Level == LanguageLevel.Native ? "Muttersprache" : german.Level.ToString();
        return note is null ? level : note.StartsWith('(') ? $"{level} {note}" : $"{level}, {note}";
    }

    private static string Join(List<string> items, string and) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} {and} {items[^1]}";

    private static void Add(List<CheatSheetItem> items, string key, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            items.Add(new(key, label, value));
        }
    }
}

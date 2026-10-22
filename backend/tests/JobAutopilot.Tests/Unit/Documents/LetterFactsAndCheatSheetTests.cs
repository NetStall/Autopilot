using FluentAssertions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;

namespace JobAutopilot.Tests.Unit.Documents;

public class LetterFactsAndCheatSheetTests
{
    private static readonly SearchProfile Search = new() { SalaryMin = 50000, SalaryMax = 60000, EarliestStart = "2026-11-01" };

    [Fact]
    public void Facts_have_stable_ids_and_come_only_from_the_profile()
    {
        var input = DocumentTestData.Input();
        var ids = input.Facts.Select(f => f.Id).ToList();

        ids.Should().Contain(["personal.name", "personal.website", "experience.years", "summary.fullstack", "exp0", "exp0.b0", "skill:csharp", "lang:german", "proj0", "edu0"]);
        input.Facts.Single(f => f.Id == "personal.website").Text.Should().Be("Website: example.org");
        input.Facts.Single(f => f.Id == "experience.years").Text.Should().Be("Berufserfahrung insgesamt: 4 Jahre");
        input.Facts.Single(f => f.Id == "exp0.b0").Text.Should().Be("Entwickelte REST-APIs mit ASP.NET Core für drei Client-Anwendungen.");
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Gaps_carry_the_honest_phrasing_in_the_letter_language()
    {
        var input = DocumentTestData.Input();

        input.Facts.Single(f => f.Id == "gap:angular").Text.Should().Be("Angular: Mit Angular habe ich noch nicht gearbeitet, aber 3 Jahre mit Vue 3 und TypeScript.");
        input.Facts.Single(f => f.Id == "gap:kubernetes").Text.Should().Contain("keine Erfahrung");

        var english = DocumentTestData.Input("Full Stack Developer", "We are looking for you. You work with Angular and Vue in our team and the role is remote.");
        english.Tone.Language.Should().Be(DocumentLanguage.En);
        english.Facts.Single(f => f.Id == "gap:angular").Text.Should().Contain("I have not worked with Angular yet");
    }

    [Fact]
    public void Salary_only_when_the_posting_asks_for_it_the_start_date_always()
    {
        // 08c: without the date the letter said „ab sofort" although I can only start later.
        var facts = DocumentTestData.Input(searchProfile: Search).Facts;
        facts.Should().NotContain(f => f.Id == "pref.salary");
        facts.Single(f => f.Id == "pref.start").Text.Should().Be("Frühester Eintritt: 01.11.2026");

        var asked = DocumentTestData.Input(description: DocumentTestData.Description + " Bitte nennen Sie Ihre Gehaltsvorstellung und den frühestmöglichen Eintrittstermin.", searchProfile: Search);
        asked.Facts.Single(f => f.Id == "pref.salary").Text.Should().Be("Gehaltsvorstellung: 50.000–60.000 € brutto/Jahr");
        asked.Facts.Single(f => f.Id == "pref.start").Text.Should().Be("Frühester Eintritt: 01.11.2026");
    }

    [Fact]
    public void Cheat_sheet_has_copyable_values_for_the_form()
    {
        var input = DocumentTestData.Input(description: DocumentTestData.Description + " Ihre Ansprechpartnerin: Frau Anna Müller");
        var letter = FakeLetterGenerator.CleanLetter(input);

        var items = CheatSheet.Build(DocumentTestData.Profile(), letter, input.Vacancy, VacancySource.Arbeitsagentur, input.Tone, Search, DocumentTestData.Today)
            .ToDictionary(i => i.Key, i => i.Value);

        items["subject"].Should().Be("Bewerbung als Full-Stack-Entwickler (m/w/d)");
        items["salary"].Should().Be("50.000–60.000 € brutto/Jahr");
        items["start"].Should().Be("01.11.2026");
        items["foundVia"].Should().Be("Jobbörse der Bundesagentur für Arbeit");
        items["german"].Should().Be("B2 (telc B2, 2024)");
        items["website"].Should().Be("https://example.org");
        items["github"].Should().Be("https://github.com/example");
        items["contact"].Should().Be("Frau Anna Müller");
    }

    [Fact]
    public void Email_text_is_short_and_follows_the_tone()
    {
        var input = DocumentTestData.Input();
        var letter = FakeLetterGenerator.CleanLetter(input);

        var sie = CheatSheet.EmailText(DocumentTestData.Profile(), letter, input.Vacancy, input.Tone, Search, DocumentTestData.Today);
        sie.Should().StartWith("Sehr geehrte Damen und Herren,\n\nanbei sende ich Ihnen meine Bewerbung als Full-Stack-Entwickler (m/w/d)");
        sie.Should().Contain("Ich habe 4 Jahre Berufserfahrung, vor allem mit ").And.Contain("Ich kann ab 01.11.2026 beginnen.");
        sie.Should().EndWith("Mit freundlichen Grüßen\nAlex Beispiel");
        Sentences(sie).Should().Be(4);

        var du = CheatSheet.EmailText(DocumentTestData.Profile(), letter with { Formality = Formality.Du, Salutation = "Hallo liebes Team," },
            input.Vacancy, input.Tone with { Formality = Formality.Du }, null, DocumentTestData.Today);
        du.Should().Contain("anbei schicke ich euch").And.Contain("Mehr zu meiner Arbeit: example.org.").And.EndWith("Viele Grüße\nAlex Beispiel");

        var en = CheatSheet.EmailText(DocumentTestData.Profile(), letter with { Language = DocumentLanguage.En, Salutation = "Dear Hiring Team," },
            input.Vacancy, input.Tone, null, DocumentTestData.Today);
        en.Should().Contain("Please find attached my application").And.EndWith("Best regards\nAlex Beispiel");
    }

    private static int Sentences(string email) =>
        email.Split("\n\n")[1].Split('\n').Length;
}

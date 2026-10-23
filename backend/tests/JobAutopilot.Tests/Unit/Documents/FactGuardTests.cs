using FluentAssertions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Tests.Fakes;

namespace JobAutopilot.Tests.Unit.Documents;

public class FactGuardTests
{
    private readonly LetterInput _input = DocumentTestData.Input();

    private IReadOnlyList<DraftWarning> Check(LetterDraft letter, int? pages = null) =>
        FactGuard.Check(letter, _input, DocumentTestData.Profile(), DocumentTestData.KnownTechnologies, pages);

    private LetterDraft Clean => FakeLetterGenerator.CleanLetter(_input);

    private LetterDraft WithParagraph(int index, string text)
    {
        var paragraphs = Clean.Paragraphs.ToList();
        paragraphs[index] = text;
        return Clean with { Paragraphs = paragraphs };
    }

    [Fact]
    public void A_letter_built_from_profile_facts_passes()
    {
        Check(Clean).Should().BeEmpty();
    }

    [Fact]
    public void An_invented_technology_is_detected()
    {
        var warnings = Check(WithParagraph(1, "Außerdem habe ich mehrere Produktivsysteme mit Kubernetes betrieben."));

        warnings.Should().ContainSingle(w => w.Code == DraftWarning.InventedTechnology)
            .Which.Should().Match<DraftWarning>(w => w.Paragraph == 1 && w.Severity == WarningSeverity.Warning && w.Message.Contains("Kubernetes"));
    }

    [Fact]
    public void A_technology_from_the_catalog_claimed_as_experience_is_detected()
    {
        var known = Core.Fit.TechnologyCatalog.All.SelectMany(t => t.AllNames());

        var warnings = FactGuard.Check(
            WithParagraph(1, "Ich habe viele Oberflächen mit DevExpress und SignalR gebaut."), _input, DocumentTestData.Profile(), known, null);

        warnings.Where(w => w.Code == DraftWarning.InventedTechnology).Select(w => w.Message)
            .Should().Contain(m => m.Contains("DevExpress")).And.Contain(m => m.Contains("SignalR"));
        FactGuard.Check(Clean, _input, DocumentTestData.Profile(), known, null).Select(w => w.Message).Should().BeEmpty("the clean letter stays clean with the catalog");
    }

    [Fact]
    public void A_known_gap_presented_as_experience_is_detected()
    {
        Check(WithParagraph(1, "Ich entwickle seit Jahren Oberflächen mit Angular."))
            .Should().Contain(w => w.Code == DraftWarning.InventedTechnology && w.Message.Contains("Angular"));
    }

    [Fact]
    public void A_learning_skill_presented_as_experience_is_detected_but_not_when_phrased_honestly()
    {
        Check(WithParagraph(1, "Docker setze ich täglich produktiv ein."))
            .Should().ContainSingle(w => w.Code == DraftWarning.LearningAsExperience);
        Check(WithParagraph(1, "Docker lerne ich gerade in eigenen Projekten."))
            .Should().NotContain(w => w.Code == DraftWarning.LearningAsExperience);
    }

    [Fact]
    public void A_gap_named_honestly_once_stays_honest_in_the_whole_paragraph()
    {
        // The real Brückenwerk letter (06c): Angular in the „Ganz offen" paragraph warned on the second sentence.
        var warnings = Check(WithParagraph(2, "Ganz offen: Mit Angular habe ich noch nicht produktiv gearbeitet. Ich arbeite aber seit drei Jahren "
            + "mit Vue 3 und TypeScript im selben komponentenbasierten Ansatz. Da ihr Angular oder Vue.js sucht, passt Vue zu meiner Erfahrung."));

        warnings.Should().NotContain(w => w.Code == DraftWarning.InventedTechnology, "a gap phrased honestly is what the letter should do");
    }

    [Fact]
    public void A_plain_no_experience_sentence_is_honest()
    {
        // Second real run (Bonuswerk, 06c).
        Check(WithParagraph(2, "Erfahrung mit Kubernetes habe ich nicht."))
            .Should().NotContain(w => w.Code == DraftWarning.InventedTechnology);
    }

    [Theory]
    [InlineData("Mein Deutsch liegt auf B1 (DTZ im Oktober 2026), ein B2-Kurs ist geplant.", false)]
    [InlineData("Im Arbeitsalltag nutze ich Englisch auf B2.", false)]
    [InlineData("Mein Deutsch ist auf C1-Niveau.", true)]
    public void Languages_are_checked_against_the_profile_levels_not_the_technologies(string sentence, bool warns)
    {
        // In the real run „Deutsch" came in with the known requirements (06c).
        var warnings = FactGuard.Check(WithParagraph(1, sentence), _input, DocumentTestData.Profile(), DocumentTestData.KnownTechnologies.Append("Deutsch").Append("Englisch"));

        warnings.Should().NotContain(w => w.Code == DraftWarning.InventedTechnology);
        warnings.Any(w => w.Code == DraftWarning.LanguageAboveProfile).Should().Be(warns, string.Join(" | ", warnings.Select(w => w.Message)));
    }

    [Fact]
    public void The_employers_own_stack_is_only_a_hint()
    {
        Check(WithParagraph(1, "Ihr Einsatz von Kubernetes klingt spannend."))
            .Should().ContainSingle(w => w.Code == DraftWarning.InventedTechnology).Which.Severity.Should().Be(WarningSeverity.Hint);
    }

    [Theory]
    [InlineData("Ich habe ein Team von 12 Entwicklern geleitet.", "12")]
    [InlineData("Ich bringe fünf Jahre Erfahrung mit.", "fünf Jahre")]
    [InlineData("Die Ladezeit sank um 40 %.", "40 %")]
    [InlineData("Die Plattform hat 1.000 Nutzer.", "1.000")]
    public void An_invented_number_or_duration_is_detected(string sentence, string shown)
    {
        Check(WithParagraph(1, sentence)).Should().ContainSingle(w => w.Code == DraftWarning.InventedNumber)
            .Which.Message.Should().Contain($"„{shown}“");
    }

    [Theory]
    [InlineData("REST-APIs für drei Client-Anwendungen.")]
    [InlineData("Seit 4 Jahren mit C#.")]
    [InlineData("Vue 3 und TypeScript, B2-Deutsch.")]
    public void Numbers_from_the_profile_or_the_posting_pass(string sentence) =>
        Check(WithParagraph(1, sentence)).Should().NotContain(w => w.Code == DraftWarning.InventedNumber);

    [Fact]
    public void A_required_gap_that_is_not_mentioned_is_detected()
    {
        var warnings = Check(WithParagraph(2, "Ich arbeite mich schnell in neue Themen ein."));

        warnings.Where(w => w.Code == DraftWarning.MissingGap).Select(w => w.Message)
            .Should().HaveCount(2).And.Contain(m => m.Contains("Angular")).And.Contain(m => m.Contains("Kubernetes"));
    }

    [Fact]
    public void Mixed_sie_and_du_is_detected()
    {
        Check(WithParagraph(3, "Ich freue mich auf dein Feedback."))
            .Should().ContainSingle(w => w.Code == DraftWarning.MixedFormality);
    }

    [Fact]
    public void A_du_letter_for_a_sie_posting_is_a_hint()
    {
        var letter = Clean with
        {
            Paragraphs = ["Dein Stack mit C#/.NET und Vue passt zu mir, mehr auf example.org.", "Ganz offen: Angular und Kubernetes kenne ich noch nicht.", "Ich freue mich auf dich."],
            Formality = Formality.Du,
        };

        Check(letter).Should().ContainSingle(w => w.Code == DraftWarning.FormalityMismatch).Which.Severity.Should().Be(WarningSeverity.Hint);
    }

    [Fact]
    public void A_letter_in_the_wrong_language_is_detected()
    {
        var letter = Clean with
        {
            Paragraphs = ["Your stack with C#/.NET and Vue is exactly what I have been working with at example.org.", "To be frank: I have not worked with Angular or Kubernetes yet."],
        };

        Check(letter).Should().Contain(w => w.Code == DraftWarning.LanguageMismatch);
    }

    [Fact]
    public void A_letter_longer_than_one_page_is_detected()
    {
        var longParagraph = string.Join(' ', Enumerable.Repeat("Bei der Muster Software GmbH habe ich REST-APIs mit ASP.NET Core entwickelt.", 20));
        var letter = Clean with { Paragraphs = [.. Clean.Paragraphs, longParagraph, longParagraph] };

        Check(letter).Should().ContainSingle(w => w.Code == DraftWarning.TooLong).Which.Message.Should().Contain("Zeichen");
        Check(Clean, pages: 2).Should().ContainSingle(w => w.Code == DraftWarning.TooLong).Which.Message.Should().Contain("2 Seiten");
        Check(letter, pages: 1).Should().NotContain(w => w.Code == DraftWarning.TooLong);
    }

    [Fact]
    public void Style_hints_for_missing_website_and_cliches()
    {
        var warnings = Check(WithParagraph(0, "Hiermit bewerbe ich mich auf Ihre Stelle mit C#/.NET und Vue."));

        warnings.Should().Contain(w => w.Code == DraftWarning.MissingWebsite && w.Severity == WarningSeverity.Hint);
        warnings.Should().Contain(w => w.Code == DraftWarning.Cliche && w.Severity == WarningSeverity.Hint && w.Paragraph == 0);
    }

    [Theory]
    [InlineData("1.000 Nutzer", "1000")]
    [InlineData("8,7 von 10", "8.7")]
    [InlineData("drei Jahre", "3")]
    [InlineData("50 %", "50")]
    public void Numbers_are_normalized(string text, string value) =>
        FactGuard.Numbers(text).Select(n => n.Value).Should().Contain(value);
}

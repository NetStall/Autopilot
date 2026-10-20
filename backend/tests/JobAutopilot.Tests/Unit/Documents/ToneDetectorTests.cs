using FluentAssertions;
using JobAutopilot.Core.Documents;

namespace JobAutopilot.Tests.Unit.Documents;

public class ToneDetectorTests
{
    [Theory]
    [InlineData("Senior Software Engineer", "We are looking for an experienced engineer to join our team. You will work with C# and Azure in our product team.", DocumentLanguage.En)]
    [InlineData("Software Engineer (m/w/d)", "Wir suchen Sie als Verstärkung für unser Team. Ihre Aufgaben sind die Entwicklung mit C#.", DocumentLanguage.De)]
    [InlineData("Backend Developer (m/w/d)", null, DocumentLanguage.De)]
    [InlineData("Softwareentwickler", null, DocumentLanguage.De)]
    [InlineData("Full Stack Developer", "You are a developer with experience in Vue and .NET. We offer remote work and your own budget for the team.", DocumentLanguage.En)]
    [InlineData("Entwickler (w/m/d) for our AI team", "Wir suchen dich für unser Team, die Arbeit ist auf Deutsch und Englisch.", DocumentLanguage.De)]
    public void Detects_the_language(string title, string? description, DocumentLanguage expected) =>
        ToneDetector.Detect(title, description).Language.Should().Be(expected);

    [Theory]
    [InlineData("Wir freuen uns auf dich! Du entwickelst mit uns neue Features.", Formality.Du)]
    [InlineData("Dein Profil: Erfahrung mit C#. Deine Aufgaben: Backend.", Formality.Du)]
    [InlineData("Wir freuen uns auf Ihre Bewerbung. Ihr Profil: Erfahrung mit C#.", Formality.Sie)]
    [InlineData("Sie entwickeln Software. Wir bieten Ihnen ein tolles Team.", Formality.Sie)]
    [InlineData("Das Team entwickelt Software mit C#.", Formality.Sie)]
    [InlineData("Ihre Aufgaben: Entwicklung. Wir freuen uns auf dich, schick uns deine Unterlagen und dein GitHub!", Formality.Du)]
    public void Detects_du_or_sie(string text, Formality expected) =>
        ToneDetector.DetectFormality(text).Should().Be(expected);

    [Theory]
    [InlineData("Ihre Ansprechpartnerin: Frau Anna Müller, Tel. 0991 123", ContactGender.Female, "Anna", "Müller")]
    [InlineData("Bewerbungen bitte z. Hd. Herrn Meier", ContactGender.Male, null, "Meier")]
    [InlineData("Ansprechpartner ist Herr Max Mustermann", ContactGender.Male, "Max", "Mustermann")]
    [InlineData("Kontakt: Frau Dr. Schulz", ContactGender.Female, null, "Schulz")]
    [InlineData("Wir freuen uns auf Ihre Bewerbung.", null, null, null)]
    public void Detects_a_named_contact(string text, ContactGender? gender, string? first, string? last)
    {
        var contact = ToneDetector.DetectContact(text);

        if (gender is null)
        {
            contact.Should().BeNull();
            return;
        }

        contact.Should().Be(new ContactPerson(gender.Value, first, last!));
    }

    [Fact]
    public void Salutation_follows_language_tone_and_contact()
    {
        var anna = new ContactPerson(ContactGender.Female, "Anna", "Müller");

        anna.Salutation(DocumentLanguage.De, Formality.Sie).Should().Be("Sehr geehrte Frau Müller,");
        anna.Salutation(DocumentLanguage.De, Formality.Du).Should().Be("Hallo Anna,");
        anna.Salutation(DocumentLanguage.En, Formality.Sie).Should().Be("Dear Ms Müller,");
        new ContactPerson(ContactGender.Male, null, "Meier").Salutation(DocumentLanguage.De, Formality.Sie).Should().Be("Sehr geehrter Herr Meier,");
        new ToneResult(DocumentLanguage.De, Formality.Sie, null).Salutation.Should().Be("Sehr geehrte Damen und Herren,");
        new ToneResult(DocumentLanguage.De, Formality.Du, null).Salutation.Should().Be("Hallo liebes Team,");
        new ToneResult(DocumentLanguage.En, Formality.Sie, null).Salutation.Should().Be("Dear Hiring Team,");
    }
}

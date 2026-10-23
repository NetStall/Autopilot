using FluentAssertions;
using JobAutopilot.Core.Applications;

namespace JobAutopilot.Tests.Unit.Applications;

public class HowToApplyTests
{
    [Theory]
    [InlineData("Bewerbung an bewerbung@firma.de senden.", "bewerbung@firma.de")]
    [InlineData("Schreib an jobs (at) firma.de", "jobs@firma.de")]
    [InlineData("karriere[at]beispiel-gmbh.de", "karriere@beispiel-gmbh.de")]
    [InlineData("hr at firma dot de", "hr@firma.de")]
    [InlineData("lisa.mueller (at) firma (dot) com", "lisa.mueller@firma.com")]
    [InlineData("Mail: Bewerbung @ Firma.DE.", "bewerbung@firma.de")]
    [InlineData("career{at}company.io", "career@company.io")]
    public void Addresses_are_found_through_common_obfuscations(string text, string expected)
    {
        HowToApplyDetector.Emails(text).Should().Equal(expected);
    }

    [Theory]
    [InlineData("Wir sind at home. Wir sind dot com.")]
    [InlineData("Version 1.2 at 3.4")]
    [InlineData("Ende des Satzes firma.de. Wir freuen uns")]
    public void Ordinary_text_is_no_address(string text)
    {
        HowToApplyDetector.Emails(text).Should().BeEmpty();
    }

    [Fact]
    public void Application_inboxes_rank_before_info_and_privacy_addresses_never_win()
    {
        HowToApplyDetector.Best(["info@firma.de", "datenschutz@firma.de", "bewerbung@firma.de"]).Should().Be("bewerbung@firma.de");
        HowToApplyDetector.Best(["info@firma.de", "l.mueller@firma.de"], "Frau Lisa Mueller").Should().Be("l.mueller@firma.de");
        HowToApplyDetector.Best(["datenschutz@firma.de", "newsletter@firma.de"]).Should().BeNull();
        HowToApplyDetector.Best(["info@firma.de"]).Should().Be("info@firma.de");
    }

    [Theory]
    [InlineData("Bewerbungen per E-Mail werden nicht berücksichtigt.")]
    [InlineData("Bitte bewerben Sie sich ausschließlich über unser Bewerberportal.")]
    [InlineData("Bewerbungen werden ausschließlich über unser Online‑Bewerbungssystem entgegengenommen.")]
    [InlineData("Bitte bewerben Sie sich ausschließlich online.")]
    [InlineData("We do not accept applications by e-mail.")]
    public void Portal_only_phrases_win_over_an_address_in_the_text(string sentence)
    {
        // Teilwerk (08a) refused an e-mail application for exactly this reason.
        var found = HowToApplyDetector.Detect($"{sentence} Fragen an info@firma.de", null);

        found.PortalOnly.Should().BeTrue();
        found.Channel.Should().Be(ApplyChannel.Portal);
        found.EmailReady(true).Should().BeFalse();
    }

    [Fact]
    public void An_email_application_is_ready_with_address_contact_and_requests()
    {
        var text = "Schick deine Unterlagen ganz klassisch per E-Mail an jobs@firma.de. Deine Ansprechpartnerin Lisa freut sich "
            + "auf deine Bewerbung mit Gehaltsvorstellung und frühestmöglichem Eintrittstermin. Datenschutz: datenschutz@firma.de";

        var found = HowToApplyDetector.Detect(text, "https://www.arbeitsagentur.de/jobsuche/jobdetail/1");

        found.Should().BeEquivalentTo(new
        {
            Channel = ApplyChannel.Email, Email = "jobs@firma.de", ContactName = "Lisa", EmailAllowed = true, PortalOnly = false,
            Source = ApplySource.Rule, ExternalUrl = (string?)null,
        });
        found.Requested.Should().Equal("Gehaltsvorstellung", "Eintrittstermin");
        found.EmailReady(true).Should().BeTrue();
        found.EmailReady(false).Should().BeFalse("without a description there is nothing to write the letter from");
    }

    [Theory]
    [InlineData("Ihre Ansprechpartnerin: Frau Anna Müller", "Frau Anna Müller", "Sehr geehrte Frau Müller,")]
    [InlineData("Fragen? Dann melde dich bei Herr Maier.", "Herr Maier", "Sehr geehrter Herr Maier,")]
    [InlineData("Deine Ansprechpartnerin Lisa freut sich auf dich.", "Lisa", null)]
    public void The_contact_person_is_found(string text, string name, string? salutation)
    {
        HowToApplyDetector.Contact(text).Should().Be((name, salutation));
    }

    [Theory]
    [InlineData("Wir freuen uns auf dich. Unser Kundenportal ist modern.", ApplyChannel.Unknown)]
    [InlineData("Dann schick uns deine Unterlagen über den Button.", ApplyChannel.Portal)]
    public void A_portal_from_the_model_needs_a_cue_in_the_text(string text, ApplyChannel expected)
    {
        // 08b spot check: 24 of 26 „Portal" answers of qwen3:8b had no cue in the posting.
        var ai = new Core.Extraction.ApplicationExtraction(ApplyChannel.Portal, null, null, null, false, false, []);

        HowToApplyDetector.Resolve(text, "https://www.arbeitsagentur.de/jobsuche/jobdetail/1", ai).Channel.Should().Be(expected);
    }

    [Fact]
    public void An_external_link_from_the_source_is_the_fallback_channel()
    {
        var found = HowToApplyDetector.Detect("Wir freuen uns auf dich.", "https://www.yourfirm.de/job/detail/YF-1/");

        found.Channel.Should().Be(ApplyChannel.External);
        found.Source.Should().Be(ApplySource.Field);
    }
}

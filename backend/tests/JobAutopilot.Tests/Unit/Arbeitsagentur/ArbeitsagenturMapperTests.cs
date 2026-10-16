using FluentAssertions;
using JobAutopilot.Infrastructure.Arbeitsagentur;

namespace JobAutopilot.Tests.Unit.Arbeitsagentur;

public class ArbeitsagenturMapperTests
{
    [Fact]
    public void Full_entry_is_mapped()
    {
        var item = Item(
            referenznummer: "10000-0000000001-S",
            plz: "94469",
            ort: "Deggendorf",
            von: "2026-10-09",
            externeUrl: "https://karriere.example.de/jobs/42?cp=BA");

        var candidate = ArbeitsagenturMapper.ToCandidate(item);

        candidate.Should().NotBeNull();
        candidate!.ExternalId.Should().Be("10000-0000000001-S");
        candidate.Title.Should().Be("Softwareentwickler C# (m/w/d)");
        candidate.Company.Should().Be("Beispiel Software GmbH");
        candidate.Location.Should().Be("94469 Deggendorf");
        candidate.Url.Should().Be("https://karriere.example.de/jobs/42?cp=BA");
        candidate.PublishedAt.Should().Be(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData(null, "Regensburg", "Regensburg")]
    [InlineData("93047", null, "93047")]
    [InlineData("  ", " Passau ", "Passau")]
    [InlineData(null, null, null)]
    [InlineData("", "", null)]
    public void Location_is_plz_and_ort_or_null(string? plz, string? ort, string? expected)
    {
        var candidate = ArbeitsagenturMapper.ToCandidate(Item(plz: plz, ort: ort));

        candidate!.Location.Should().Be(expected);
    }

    [Fact]
    public void Location_is_null_without_any_stellenlokation()
    {
        var item = Item() with { Stellenlokationen = null };

        ArbeitsagenturMapper.ToCandidate(item)!.Location.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://karriere.example.de/jobs/42")]
    [InlineData("/jobs/42")]
    [InlineData("karriere.example.de/jobs/42")]
    [InlineData("ftp://example.de/job.txt")]
    public void Url_falls_back_to_the_public_detail_page(string? externeUrl)
    {
        var candidate = ArbeitsagenturMapper.ToCandidate(Item(referenznummer: "10000-0000000001-S", externeUrl: externeUrl));

        candidate!.Url.Should().Be("https://www.arbeitsagentur.de/jobsuche/jobdetail/10000-0000000001-S");
    }

    [Fact]
    public void Reference_number_is_url_encoded_in_the_detail_page()
    {
        var candidate = ArbeitsagenturMapper.ToCandidate(Item(referenznummer: "12265-485776_JB 52/68#ä-S"));

        candidate!.ExternalId.Should().Be("12265-485776_JB 52/68#ä-S");
        candidate.Url.Should().Be(
            "https://www.arbeitsagentur.de/jobsuche/jobdetail/12265-485776_JB%2052%2F68%23%C3%A4-S");
    }

    [Theory]
    [InlineData("2026-10-09", 2)] // summer time (CEST)
    [InlineData("2026-12-01", 1)] // winter time (CET)
    public void Date_is_midnight_in_Berlin(string von, int expectedOffsetHours)
    {
        var candidate = ArbeitsagenturMapper.ToCandidate(Item(von: von));

        var expectedDate = DateOnly.ParseExact(von, "yyyy-MM-dd");
        candidate!.PublishedAt.Should().Be(
            new DateTimeOffset(expectedDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(expectedOffsetHours)));
    }

    [Fact]
    public void Date_falls_back_to_first_publication()
    {
        var item = Item() with { Veroeffentlichungszeitraum = null, DatumErsteVeroeffentlichung = "2026-10-01" };

        ArbeitsagenturMapper.ToCandidate(item)!.PublishedAt
            .Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("09.10.2026")]
    [InlineData("2026-13-01")]
    public void Missing_or_invalid_date_is_null(string? von)
    {
        var item = Item(von: von) with { DatumErsteVeroeffentlichung = null };

        ArbeitsagenturMapper.ToCandidate(item)!.PublishedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Titel")]
    [InlineData("  ", "Titel")]
    [InlineData("10000-0000000001-S", null)]
    [InlineData("10000-0000000001-S", " ")]
    public void Entries_without_reference_number_or_title_are_skipped(string? referenznummer, string? titel)
    {
        var item = Item() with { Referenznummer = referenznummer, StellenangebotsTitel = titel };

        ArbeitsagenturMapper.ToCandidate(item).Should().BeNull();
    }

    [Fact]
    public void Missing_company_gets_a_placeholder()
    {
        var item = Item() with { Firma = null };

        ArbeitsagenturMapper.ToCandidate(item)!.Company.Should().Be(ArbeitsagenturMapper.UnknownCompany);
    }

    [Fact]
    public void Overlong_title_is_truncated_to_the_column_length()
    {
        var item = Item() with { StellenangebotsTitel = new string('x', 400) };

        ArbeitsagenturMapper.ToCandidate(item)!.Title.Should().HaveLength(300);
    }

    private static JobSearchItem Item(
        string? referenznummer = "10000-0000000001-S",
        string? plz = "94469",
        string? ort = "Deggendorf",
        string? von = "2026-10-09",
        string? externeUrl = null) =>
        new(
            referenznummer,
            "Softwareentwickler C# (m/w/d)",
            "Beispiel Software GmbH",
            [new JobLocation(new JobAddress(plz, ort))],
            new JobPeriod(von),
            von,
            externeUrl);
}

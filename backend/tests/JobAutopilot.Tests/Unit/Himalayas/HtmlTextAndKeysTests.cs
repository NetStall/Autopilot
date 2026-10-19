using FluentAssertions;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Himalayas;

namespace JobAutopilot.Tests.Unit.Himalayas;

public class HtmlTextAndKeysTests
{
    [Theory]
    [InlineData("<p>Hallo&nbsp;Welt</p>", "Hallo Welt")]
    [InlineData("<p>A</p><p>B</p>", "A\n\nB")]
    [InlineData("Zeile 1<br>Zeile 2<br/>Zeile 3", "Zeile 1\nZeile 2\nZeile 3")]
    [InlineData("<ul><li>C#</li><li>Vue</li></ul>", "- C#\n- Vue")]
    [InlineData("<img src=x onerror=alert(1)>Text<script>evil()</script><style>p{}</style>", "Text")]
    [InlineData("&lt;b&gt;kein Tag&lt;/b&gt; &amp; mehr", "<b>kein Tag</b> & mehr")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Html_becomes_plain_text(string? html, string? expected) =>
        HtmlText.ToPlainText(html).Should().Be(expected);

    [Theory]
    [InlineData("Beispiel Software GmbH", "Senior .NET Developer (m/w/d)", "Beispiel Software", "Senior .NET Developer")]
    [InlineData("BEISPIEL SOFTWARE GMBH & CO. KG", "Senior .NET Developer (w/m/d)", "beispiel software", "senior .net developer")]
    [InlineData("Beispiel Software AG", "Senior .NET-Developer m/w/d", "Beispiel Software", "Senior .NET Developer")]
    [InlineData("Beispiel Software SE", "Senior .NET Developer (all genders)", "Beispiel Software", "senior .net developer")]
    [InlineData("Beispiel e.V.", "C# Entwickler (m/f/x)", "Beispiel", "C# Entwickler")]
    public void Duplicate_key_ignores_case_gender_markers_and_legal_forms(
        string companyA, string titleA, string companyB, string titleB) =>
        DuplicateKeys.For(companyA, titleA).Should().Be(DuplicateKeys.For(companyB, titleB));

    [Theory]
    [InlineData("Beispiel GmbH", "C# Entwickler", "Beispiel GmbH", "C++ Entwickler")]
    [InlineData("Beispiel GmbH", "Senior .NET Developer", "Andere GmbH", "Senior .NET Developer")]
    [InlineData("Beispiel GmbH", "Junior .NET Developer", "Beispiel GmbH", "Senior .NET Developer")]
    public void Different_jobs_get_different_keys(string companyA, string titleA, string companyB, string titleB) =>
        DuplicateKeys.For(companyA, titleA).Should().NotBe(DuplicateKeys.For(companyB, titleB));

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "Germany" }, null)]
    [InlineData(new[] { "Europe" }, null)]
    [InlineData(new[] { "EMEA", "UK" }, null)]
    [InlineData(new[] { "Spain", "Germany", "France" }, null)]
    [InlineData(new[] { "United States" }, "Nur für Bewerber aus: United States")]
    [InlineData(new[] { "Brazil", "Mexico", "Argentina" }, "Nur für Bewerber aus: Brazil, Mexico, Argentina")]
    public void Only_jobs_open_to_germany_eu_or_worldwide_are_kept(string[] restrictions, string? exclusion)
    {
        var job = new HimalayasJob("https://himalayas.app/companies/x/jobs/dev-1", "Dev", "X", "https://himalayas.app/a",
            null, restrictions, null, null);

        HimalayasMapper.ToCandidate(job, "DE")!.ExclusionReason.Should().Be(exclusion);
    }

    [Fact]
    public void Key_has_a_readable_form() =>
        DuplicateKeys.For("Muster GmbH & Co. KG", "Softwareentwickler C#/.NET (m/w/d)")
            .Should().Be("muster|softwareentwickler c# net");
}

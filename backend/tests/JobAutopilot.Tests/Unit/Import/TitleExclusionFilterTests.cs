using FluentAssertions;
using JobAutopilot.Core.Import;

namespace JobAutopilot.Tests.Unit.Import;

public class TitleExclusionFilterTests
{
    private static readonly TitleExclusionFilter Filter = new([
        "Ausbildung", "Azubi", "Praktikum", "Werkstudent", "Duales Studium", "Schülerpraktikum", "Minijob",
    ]);

    [Theory]
    [InlineData("Ausbildung zum Fachinformatiker (m/w/d)")]
    [InlineData("AUSBILDUNG Fachinformatiker Anwendungsentwicklung 2027")]
    [InlineData("Fachinformatiker – Ausbildung/Studium")]
    [InlineData("Azubi Fachinformatiker (m/w/d)")]
    [InlineData("Praktikum Softwareentwicklung")]
    [InlineData("Werkstudent (m/w/d) Webentwicklung")]
    [InlineData("Werkstudent*in Softwareentwicklung")]
    [InlineData("Duales Studium Informatik")]
    [InlineData("duales  studium Wirtschaftsinformatik")]
    [InlineData("SCHÜLERPRAKTIKUM IT")]
    [InlineData("Friseur/in (m/w/d) auf Minijob Basis")]
    [InlineData("(Ausbildung) IT")]
    public void Excludes_titles_with_a_term_as_whole_word_or_phrase(string title)
    {
        Filter.IsExcluded(title).Should().BeTrue();
    }

    [Theory]
    [InlineData("Ausbildungsleiter IT (m/w/d)")]
    [InlineData("Senior .NET Entwickler mit Ausbildungsbefugnis")]
    [InlineData("Praktikumsbetreuer Softwareentwicklung")]
    [InlineData("Duales Studienangebot – Teamleitung")]
    [InlineData("Werkstudentin Data")]
    [InlineData("Softwareentwickler C# (m/w/d)")]
    public void Keeps_titles_where_the_term_is_only_part_of_a_word(string title)
    {
        Filter.IsExcluded(title).Should().BeFalse();
    }

    [Fact]
    public void Regex_characters_in_terms_are_literal()
    {
        var filter = new TitleExclusionFilter(["C++ (Junior)"]);

        filter.IsExcluded("C++ (Junior) Entwickler").Should().BeTrue();
        filter.IsExcluded("C Junior Entwickler").Should().BeFalse();
    }

    [Fact]
    public void Empty_term_list_excludes_nothing()
    {
        TitleExclusionFilter.None.IsExcluded("Ausbildung").Should().BeFalse();
        new TitleExclusionFilter(["", "  "]).IsExcluded("Ausbildung").Should().BeFalse();
    }
}

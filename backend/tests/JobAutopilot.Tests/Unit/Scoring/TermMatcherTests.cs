using FluentAssertions;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>Word-boundary traps of keyword matching.</summary>
public class TermMatcherTests
{
    [Theory]
    // Java vs JavaScript
    [InlineData("Java", "Java-Entwickler (m/w/d)", true)]
    [InlineData("Java", "Erfahrung mit Java/Kotlin", true)]
    [InlineData("Java", "JavaScript-Entwickler (m/w/d)", false)]
    [InlineData("Java", "Kenntnisse in javascript und TypeScript", false)]
    [InlineData("JavaScript", "JavaScript-Entwickler", true)]
    // C# vs C++ vs C
    [InlineData("C#", "Softwareentwickler C#/.NET", true)]
    [InlineData("C#", "C#-Entwickler (m/w/d)", true)]
    [InlineData("C#", "Entwickler C++ (m/w/d)", false)]
    [InlineData("C++", "Entwickler C++ (m/w/d)", true)]
    [InlineData("C++", "Softwareentwickler C# (m/w/d)", false)]
    [InlineData("C", "Führerschein Klasse C#", false)]
    // .NET vs ASP.NET vs Netzwerk vs domains
    [InlineData(".NET", ".NET-Entwickler", true)]
    [InlineData(".NET", "Erfahrung mit C#.NET", true)]
    [InlineData(".NET", "Netzwerkadministrator (m/w/d)", false)]
    [InlineData(".NET", "Bewerbung an jobs@example.net", false)]
    [InlineData(".NET", "Wir nutzen ASP.NET Core", false)]
    [InlineData("ASP.NET", "Wir nutzen ASP.NET Core", true)]
    // Go as a word vs inside words
    [InlineData("Golang", "Backend Engineer (Golang)", true)]
    [InlineData("Go Developer", "Senior Go-Developer (m/w/d)", true)]
    [InlineData("Go Developer", "Google Developer Expert", false)]
    [InlineData("Go Entwickler", "Unterstützung beim Go-Live, Entwickler (m/w/d)", false)]
    [InlineData("Golang", "MongoDB und Django", false)]
    // Spaces and hyphens are flexible
    [InlineData("Full Stack", "Fullstack Developer", true)]
    [InlineData("Full Stack", "Full-Stack-Entwickler", true)]
    [InlineData("SQL Server", "MS SQL-Server", true)]
    // Numbers
    [InlineData("{n} Tage Homeoffice", "bis zu 3 Tage Homeoffice pro Woche", true)]
    [InlineData("{n} % remote", "80% remote möglich", true)]
    [InlineData("100 % remote", "zu 100% remote", true)]
    [InlineData("100 % remote", "1100 % remote", false)]
    public void Matches_whole_words_only(string term, string text, bool expected)
    {
        var matcher = new TermMatcher([term]);

        (matcher.FindFirst(text) is not null).Should().Be(expected);
    }

    [Theory]
    [InlineData("Wir bieten eine RESTful API", true)]
    [InlineData("REST-Schnittstellen", true)]
    [InlineData("the rest of the team", false)]
    [InlineData("Rest des Tages", false)]
    public void Match_case_keeps_acronyms_apart_from_ordinary_words(string text, bool expected)
    {
        var matcher = new TermMatcher(["REST", "RESTful"], matchCase: true);

        (matcher.FindFirst(text) is not null).Should().Be(expected);
    }

    [Fact]
    public void Returns_the_first_configured_term_that_is_found()
    {
        var matcher = new TermMatcher(["ASP.NET", ".NET", "dotnet"]);

        matcher.FindFirst("ASP.NET Core und .NET 10").Should().Be("ASP.NET");
        matcher.FindFirst(null).Should().BeNull();
        matcher.FindFirst(string.Empty).Should().BeNull();
    }
}

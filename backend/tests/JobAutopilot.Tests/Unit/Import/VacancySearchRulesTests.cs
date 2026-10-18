using System.Text;
using FluentAssertions;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Import;
using Microsoft.Extensions.Configuration;

namespace JobAutopilot.Tests.Unit.Import;

public class VacancySearchRulesTests
{
    private static VacancySearch Valid(string name = "region-csharp") => new()
    {
        Name = name,
        Keyword = "Softwareentwickler C#",
        Location = "Deggendorf",
        RadiusKm = 70,
    };

    [Fact]
    public void Defaults_are_30_days_not_remote_and_no_page_override()
    {
        var search = new VacancySearch { Name = "x", Keyword = "C#" };

        search.PublishedWithinDays.Should().Be(30);
        search.RemoteOnly.Should().BeFalse();
        search.MaxPages.Should().BeNull();
        search.Location.Should().BeNull("no location = all of Germany");
    }

    [Fact]
    public void Valid_region_and_remote_searches_pass()
    {
        var remote = new VacancySearch { Name = "remote-dotnet", Keyword = ".NET", RemoteOnly = true };
        var regionAndRemote = Valid("region-remote") with { RemoteOnly = true };

        VacancySearchRules.Validate([Valid(), remote, regionAndRemote]).Should().BeEmpty();
    }

    [Fact]
    public void Names_must_be_unique_ignoring_case()
    {
        var errors = VacancySearchRules.Validate([Valid("remote-dotnet"), Valid("Remote-DotNet"), Valid("other")]);

        errors.Should().ContainSingle().Which.Should().Contain("remote-dotnet").And.Contain("unique");
    }

    [Fact]
    public void Radius_without_location_is_an_error()
    {
        var errors = VacancySearchRules.Validate(Valid() with { Location = null });

        errors.Should().ContainSingle().Which.Should().Contain("RadiusKm requires a Location");
    }

    [Theory]
    [InlineData("", "Name is required")]
    [InlineData("region csharp", "Name must be")]
    [InlineData("région", "Name must be")]
    public void Name_must_be_a_short_label(string name, string expected)
    {
        VacancySearchRules.Validate(Valid(name)).Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Theory]
    [InlineData(-1, null, null, "RadiusKm")]
    [InlineData(201, null, null, "RadiusKm")]
    [InlineData(70, 101, null, "PublishedWithinDays")]
    [InlineData(70, -1, null, "PublishedWithinDays")]
    [InlineData(70, 30, 0, "MaxPages")]
    [InlineData(70, 30, 51, "MaxPages")]
    public void Ranges_are_checked(int radius, int? days, int? maxPages, string field)
    {
        var search = Valid() with { RadiusKm = radius, PublishedWithinDays = days ?? 30, MaxPages = maxPages };

        VacancySearchRules.Validate(search).Should().ContainSingle().Which.Should().Contain(field);
    }

    [Fact]
    public void Keyword_is_required()
    {
        VacancySearchRules.Validate(Valid() with { Keyword = " " }).Should().ContainSingle()
            .Which.Should().Contain("Keyword");
    }

    [Fact]
    public void Configuration_binding_applies_the_defaults()
    {
        const string json = """
            {
              "Import": {
                "Searches": [
                  { "Name": "region-csharp", "Keyword": "C#", "Location": "Deggendorf", "RadiusKm": 70 },
                  { "Name": "remote-dotnet", "Keyword": ".NET", "RemoteOnly": true, "MaxPages": 3 }
                ],
                "ExcludeTitleTerms": [ "Ausbildung", "Duales Studium" ]
              }
            }
            """;
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

        var options = configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>()!;

        options.Searches.Should().HaveCount(2);
        options.Searches[0].Should().Be(new VacancySearch
        {
            Name = "region-csharp", Keyword = "C#", Location = "Deggendorf", RadiusKm = 70, PublishedWithinDays = 30,
        });
        options.Searches[1].Should().Be(new VacancySearch
        {
            Name = "remote-dotnet", Keyword = ".NET", RemoteOnly = true, MaxPages = 3,
        });
        options.ExcludeTitleTerms.Should().Equal("Ausbildung", "Duales Studium");
        new ImportOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Options_validator_reports_invalid_searches_and_empty_terms()
    {
        var options = new ImportOptions
        {
            Searches = [Valid("a"), Valid("a") with { Location = null }],
            ExcludeTitleTerms = ["Ausbildung", " "],
        };

        var result = new ImportOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
        result.Failures.Should().Contain(f => f.Contains("unique"))
            .And.Contain(f => f.Contains("RadiusKm requires a Location"))
            .And.Contain(f => f.Contains("ExcludeTitleTerms"));
    }
}

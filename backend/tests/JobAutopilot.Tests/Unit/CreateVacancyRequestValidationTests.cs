using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit;

public class CreateVacancyRequestValidationTests
{
    [Fact]
    public void Valid_request_has_no_errors()
    {
        var request = new CreateVacancyRequest
        {
            Title = "Fullstack Developer",
            Company = "Beispiel GmbH",
            Url = "https://example.de/jobs/3",
        };

        Validate(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Firma", nameof(CreateVacancyRequest.Title))]
    [InlineData("   ", "Firma", nameof(CreateVacancyRequest.Title))]
    [InlineData("Dev", "", nameof(CreateVacancyRequest.Company))]
    public void Title_and_company_are_required(string title, string company, string expectedMember)
    {
        var request = new CreateVacancyRequest { Title = title, Company = company };

        Validate(request).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(expectedMember);
    }

    [Fact]
    public void Title_longer_than_the_limit_is_rejected()
    {
        var request = new CreateVacancyRequest
        {
            Title = new string('a', VacancyLimits.TitleMaxLength + 1),
            Company = "Firma",
        };

        Validate(request).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(nameof(CreateVacancyRequest.Title));
    }

    [Fact]
    public void Url_must_be_absolute()
    {
        var request = new CreateVacancyRequest { Title = "Dev", Company = "Firma", Url = "jobs/3" };

        Validate(request).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(nameof(CreateVacancyRequest.Url));
    }

    private static List<ValidationResult> Validate(CreateVacancyRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}

using FluentAssertions;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit;

public class VacancyMappingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ToDto_copies_every_field()
    {
        var vacancy = new Vacancy
        {
            Id = Guid.NewGuid(),
            Source = VacancySource.Arbeitsagentur,
            ExternalId = "10000-1234567890-S",
            Title = "Backend Developer (m/w/d)",
            Company = "Beispiel GmbH",
            Location = "Leipzig",
            Url = "https://example.de/jobs/1",
            Description = "C#, .NET, PostgreSQL",
            EmployerType = EmployerType.Direct,
            PublishedAt = Now.AddDays(-2),
            FetchedAt = Now,
            IsSeen = true,
        };

        var dto = vacancy.ToDto();

        // ExpiresAt, DuplicateKey and RemoteHint are internal (expiry clean-up, de-duplication) and not part of the DTO.
        dto.Should().BeEquivalentTo(vacancy, o => o.Excluding(v => v.ExpiresAt).Excluding(v => v.DuplicateKey).Excluding(v => v.RemoteHint));
    }

    [Fact]
    public void ToManualVacancy_sets_source_external_id_and_fetch_time()
    {
        var request = new CreateVacancyRequest
        {
            Title = "  .NET Entwickler  ",
            Company = "Beispiel AG",
            Location = "   ",
            Url = "https://example.de/jobs/2",
            EmployerType = EmployerType.Agency,
        };

        var vacancy = request.ToManualVacancy(Now);

        vacancy.Id.Should().NotBeEmpty();
        vacancy.Source.Should().Be(VacancySource.Manual);
        vacancy.ExternalId.Should().Be($"manual-{vacancy.Id:N}");
        vacancy.Title.Should().Be(".NET Entwickler");
        vacancy.Company.Should().Be("Beispiel AG");
        vacancy.Location.Should().BeNull("blank optional fields are stored as null");
        vacancy.Url.Should().Be("https://example.de/jobs/2");
        vacancy.EmployerType.Should().Be(EmployerType.Agency);
        vacancy.FetchedAt.Should().Be(Now);
        vacancy.IsSeen.Should().BeFalse();
    }

    [Fact]
    public void ToManualVacancy_generates_a_new_external_id_each_time()
    {
        var request = new CreateVacancyRequest { Title = "Dev", Company = "Firma" };

        var first = request.ToManualVacancy(Now);
        var second = request.ToManualVacancy(Now);

        first.ExternalId.Should().NotBe(second.ExternalId);
    }
}

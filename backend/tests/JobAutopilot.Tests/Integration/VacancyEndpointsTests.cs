using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Tests.Integration;

public class VacancyEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task List_is_empty_when_there_are_no_vacancies()
    {
        var page = await GetPageAsync("/api/vacancies");

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(VacancyListQuery.DefaultPageSize);
    }

    [Fact]
    public async Task Create_stores_a_manual_vacancy_and_returns_it()
    {
        var request = new CreateVacancyRequest
        {
            Title = "Senior .NET Developer",
            Company = "Beispiel GmbH",
            Location = "Berlin",
            Url = "https://example.de/jobs/1",
            EmployerType = EmployerType.Direct,
        };

        var response = await Client.PostAsJsonAsync("/api/vacancies", request, JsonOptions, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<VacancyDto>(JsonOptions, CancellationToken);
        created.Should().NotBeNull();
        created!.Source.Should().Be(VacancySource.Manual);
        created.ExternalId.Should().StartWith("manual-");
        created.Title.Should().Be(request.Title);
        created.EmployerType.Should().Be(EmployerType.Direct);
        created.FetchedAt.Should().Be(Factory.Clock.GetUtcNow());
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/vacancies/{created.Id}");

        await using var db = Postgres.CreateDbContext();
        var stored = await db.Vacancies.SingleAsync(CancellationToken);
        stored.Id.Should().Be(created.Id);
        stored.Company.Should().Be("Beispiel GmbH");
    }

    [Fact]
    public async Task Create_accepts_a_published_date_with_a_non_utc_offset()
    {
        var publishedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromHours(2));
        var request = new CreateVacancyRequest { Title = "Dev", Company = "Firma", PublishedAt = publishedAt };

        var response = await Client.PostAsJsonAsync("/api/vacancies", request, JsonOptions, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var db = Postgres.CreateDbContext();
        var stored = await db.Vacancies.SingleAsync(CancellationToken);
        stored.PublishedAt.Should().Be(publishedAt, "the same instant is stored, normalised to UTC");
        stored.PublishedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Create_returns_validation_problem_for_missing_fields()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/vacancies", new { title = "", url = "not a url" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().BeEquivalentTo("Title", "Company", "Url");

        await using var db = Postgres.CreateDbContext();
        (await db.Vacancies.CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Create_returns_bad_request_for_an_unknown_employer_type()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/vacancies", new { title = "Dev", company = "Firma", employerType = "Recruiter" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task List_returns_newest_first_and_pages()
    {
        foreach (var title in new[] { "First", "Second", "Third" })
        {
            var response = await Client.PostAsJsonAsync(
                "/api/vacancies", new CreateVacancyRequest { Title = title, Company = "Firma" }, JsonOptions, CancellationToken);
            response.EnsureSuccessStatusCode();
            Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var firstPage = await GetPageAsync("/api/vacancies?page=1&pageSize=2");
        var secondPage = await GetPageAsync("/api/vacancies?page=2&pageSize=2");

        firstPage.TotalCount.Should().Be(3);
        firstPage.Items.Select(v => v.Title).Should().Equal("Third", "Second");
        secondPage.Items.Select(v => v.Title).Should().Equal("First");
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_rejects_invalid_paging(string query)
    {
        var response = await Client.GetAsync($"/api/vacancies?{query}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<PagedResponse<VacancyDto>> GetPageAsync(string url)
    {
        var page = await Client.GetFromJsonAsync<PagedResponse<VacancyDto>>(url, JsonOptions, CancellationToken);
        page.Should().NotBeNull();
        return page!;
    }
}

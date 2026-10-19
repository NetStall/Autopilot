using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>Detail, PATCH and bulk "seen" endpoints.</summary>
public class VacancySeenTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Detail_returns_the_vacancy()
    {
        var vacancy = Make(title: "Backend Developer");
        await SeedAsync(vacancy);

        var dto = await Client.GetFromJsonAsync<VacancyDto>($"/api/vacancies/{vacancy.Id}", JsonOptions, CancellationToken);

        dto!.Title.Should().Be("Backend Developer");
        dto.ExternalId.Should().Be(vacancy.ExternalId);
    }

    [Fact]
    public async Task Detail_of_an_unknown_id_is_404()
    {
        var response = await Client.GetAsync($"/api/vacancies/{Guid.NewGuid()}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Patch_sets_is_seen(bool before, bool after)
    {
        var vacancy = Make(isSeen: before);
        var other = Make(isSeen: before);
        await SeedAsync(vacancy, other);

        var response = await PatchAsync(vacancy.Id, $$"""{ "isSeen": {{(after ? "true" : "false")}} }""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await IsSeenAsync(vacancy.Id)).Should().Be(after);
        (await IsSeenAsync(other.Id)).Should().Be(before, "only the addressed vacancy changes");
    }

    [Fact]
    public async Task Patch_of_an_unknown_id_is_404()
    {
        var response = await PatchAsync(Guid.NewGuid(), """{ "isSeen": true }""");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_without_any_field_is_a_validation_problem()
    {
        var vacancy = Make();
        await SeedAsync(vacancy);

        var response = await PatchAsync(vacancy.Id, "{}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().BeEquivalentTo("IsSeen", "Interest");
    }

    [Fact]
    public async Task Patch_rejects_other_fields()
    {
        var vacancy = Make(title: "Original");
        await SeedAsync(vacancy);

        var response = await PatchAsync(vacancy.Id, """{ "isSeen": true, "title": "Hacked" }""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = Postgres.CreateDbContext();
        var stored = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id, CancellationToken);
        stored.Title.Should().Be("Original");
        stored.IsSeen.Should().BeFalse();
    }

    [Fact]
    public async Task Bulk_marks_the_given_vacancies_as_seen_and_ignores_unknown_ids()
    {
        var a = Make();
        var b = Make();
        var untouched = Make();
        await SeedAsync(a, b, untouched);

        var response = await Client.PostAsJsonAsync(
            "/api/vacancies/seen", new { ids = new[] { a.Id, b.Id, a.Id, Guid.NewGuid() } }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await IsSeenAsync(a.Id)).Should().BeTrue();
        (await IsSeenAsync(b.Id)).Should().BeTrue();
        (await IsSeenAsync(untouched.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Bulk_accepts_exactly_the_maximum()
    {
        var ids = Enumerable.Range(0, MarkSeenRequest.MaxIds).Select(_ => Guid.NewGuid()).ToArray();

        var response = await Client.PostAsJsonAsync("/api/vacancies/seen", new { ids }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MarkSeenRequest.MaxIds + 1)]
    public async Task Bulk_rejects_empty_or_too_many_ids(int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        var response = await Client.PostAsJsonAsync("/api/vacancies/seen", new { ids }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().Equal("Ids");
    }

    [Fact]
    public async Task Bulk_without_ids_is_a_validation_problem()
    {
        var response = await Client.PostAsJsonAsync("/api/vacancies/seen", new { }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpResponseMessage> PatchAsync(Guid id, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PatchAsync($"/api/vacancies/{id}", content, CancellationToken);
    }

    private async Task<bool> IsSeenAsync(Guid id)
    {
        await using var db = Postgres.CreateDbContext();
        return await db.Vacancies.Where(v => v.Id == id).Select(v => v.IsSeen).SingleAsync(CancellationToken);
    }

    private async Task SeedAsync(params Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }
}

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Scoring;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>Relevance and remote level through the API: filters, sorting, re-scoring, details after an import.</summary>
public class RelevanceApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private readonly FakeVacancySource _source = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
    }

    [Fact]
    public async Task Default_sort_is_by_score_with_direct_employers_before_agencies_at_the_same_score()
    {
        await SeedAsync(
            Scored("Agentur 80", 80, EmployerType.Agency),
            Scored("Direkt 80", 80, EmployerType.Direct),
            Scored("Zeitarbeit 80", 80, EmployerType.Temporary),
            Scored("Unbekannt 80", 80, EmployerType.Unknown),
            Scored("Agentur 90", 90, EmployerType.Agency),
            Scored("Ohne Score", null, EmployerType.Direct));

        var page = await GetAsync(string.Empty);

        page.Items.Select(v => v.Title).Should().Equal(
            "Agentur 90", "Direkt 80", "Unbekannt 80", "Agentur 80", "Zeitarbeit 80", "Ohne Score");
    }

    [Fact]
    public async Task Relevance_filter_is_repeatable_and_unscored_counts_as_maybe()
    {
        await SeedAsync(
            Scored("R", 80, level: RelevanceLevel.Relevant),
            Scored("M", 40, level: RelevanceLevel.Maybe),
            Scored("I", 10, level: RelevanceLevel.Irrelevant),
            Scored("U", null));

        (await GetAsync("?relevance=Relevant&relevance=Maybe")).Items.Select(v => v.Title).Should().Equal("R", "M", "U");
        (await GetAsync("?relevance=irrelevant")).Items.Select(v => v.Title).Should().Equal("I");
        (await GetAsync(string.Empty)).TotalCount.Should().Be(4, "without the filter nothing is hidden");
        (await Client.GetAsync("/api/vacancies?relevance=Top", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_leaves_out_descriptions_and_the_detail_contains_description_and_reasons()
    {
        var vacancy = Scored("A", 70);
        vacancy.Description = "Langer Text";
        vacancy.RelevanceReasons = ["C# im Titel ✓ (+50)"];
        await SeedAsync(vacancy);

        var listed = (await GetAsync(string.Empty)).Items.Single();
        var detail = await Client.GetFromJsonAsync<VacancyDto>($"/api/vacancies/{vacancy.Id}", ApiJson, CancellationToken);

        listed.Description.Should().BeNull();
        listed.RelevanceReasons.Should().Equal("C# im Titel ✓ (+50)");
        detail!.Description.Should().Be("Langer Text");
        detail.RelevanceScore.Should().Be(70);
    }

    [Fact]
    public async Task Rescore_endpoint_scores_all_vacancies_in_the_background()
    {
        await SeedAsync(Make(title: "Softwareentwickler C# (m/w/d)"), Make(title: "Java Developer (m/w/d)"));

        var response = await Client.PostAsync("/api/vacancies/rescore", content: null, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var status = await WaitForRescoreAsync();
        status.LastScored.Should().Be(2);
        await using var db = Postgres.CreateDbContext();
        var stored = await db.Vacancies.OrderBy(v => v.Title).ToListAsync(CancellationToken);
        stored.Select(v => v.RelevanceLevel).Should().Equal(RelevanceLevel.Irrelevant, RelevanceLevel.Relevant);
        stored.Should().OnlyContain(v => v.RelevanceReasons.Count > 0);
    }

    [Fact]
    public async Task Import_fetches_details_afterwards_and_scores_with_the_description()
    {
        _source.Returns("s", Candidate("A-1", "Softwareentwickler (m/w/d)"));
        Factory.DetailsSource.Returns("A-1", new VacancyDetails(
            "Wir entwickeln mit C#, .NET und Vue.js. Die Stelle ist 100 % remote.", EmployerHint: EmployerType.Direct));

        var run = await RunImportAsync("""{ "name": "s", "keyword": "C#" }""");

        run.Status.Should().Be(ImportRunStatus.Succeeded);
        var vacancy = (await GetAsync(string.Empty)).Items.Single();
        vacancy.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
        vacancy.RelevanceLevel.Should().Be(RelevanceLevel.Relevant);
        vacancy.DetailsFetchedAt.Should().NotBeNull();
        Factory.DetailsSource.Requested.Should().Equal("A-1");

        await RunImportAsync("""{ "name": "s", "keyword": "C#" }""");
        Factory.DetailsSource.Requested.Should().Equal(["A-1"], "details are cached until the posting changes");
    }

    [Fact]
    public async Task Migration_maps_is_remote_to_at_least_hybrid()
    {
        var connectionString = new Npgsql.NpgsqlConnectionStringBuilder(Postgres.ConnectionString)
        {
            Database = $"jobautopilot_migration_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new Infrastructure.Persistence.AppDbContext(
            new DbContextOptionsBuilder<Infrastructure.Persistence.AppDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261018195555_AddImportRunsAndVacancyInterest", CancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Vacancies" ("Id", "Source", "ExternalId", "Title", "Company", "EmployerType", "FetchedAt", "IsSeen", "IsRemote", "MatchedSearches", "Interest")
                VALUES (gen_random_uuid(), 'Arbeitsagentur', 'R', 'Remote', 'Firma', 'Unknown', now(), false, true, ARRAY['remote-csharp'], 'None'),
                       (gen_random_uuid(), 'Arbeitsagentur', 'N', 'Region', 'Firma', 'Unknown', now(), false, false, ARRAY[]::text[], 'None');
                """,
                CancellationToken);

            await migrator.MigrateAsync(cancellationToken: CancellationToken);

            var levels = await db.Vacancies.OrderBy(v => v.ExternalId)
                .Select(v => new { v.ExternalId, v.RemoteLevel, v.RelevanceScore, v.RelevanceReasons })
                .ToListAsync(CancellationToken);
            levels.Select(l => (l.ExternalId, l.RemoteLevel)).Should().Equal(("N", RemoteLevel.Unknown), ("R", RemoteLevel.Hybrid));
            levels.Should().OnlyContain(l => l.RelevanceScore == null && l.RelevanceReasons.Count == 0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken);
        }
    }

    private async Task<RescoreStatus> WaitForRescoreAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var status = (await Client.GetFromJsonAsync<RescoreStatus>("/api/vacancies/rescore", ApiJson, CancellationToken))!;
            if (status is { State: RescoreState.Idle, LastFinishedAt: not null })
            {
                return status;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Re-scoring still {status.State}.");
            }

            await Task.Delay(50, CancellationToken);
        }
    }

    private static Vacancy Scored(
        string title,
        int? score,
        EmployerType employerType = EmployerType.Unknown,
        RelevanceLevel? level = null)
    {
        var vacancy = Make(title: title, employerType: employerType);
        vacancy.RelevanceScore = score;
        vacancy.RelevanceLevel = score is null ? null : level ?? RelevanceLevel.Relevant;
        return vacancy;
    }

    private async Task SeedAsync(params Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<PagedResponse<VacancyDto>> GetAsync(string query) =>
        (await Client.GetFromJsonAsync<PagedResponse<VacancyDto>>($"/api/vacancies{query}", ApiJson, CancellationToken))!;
}

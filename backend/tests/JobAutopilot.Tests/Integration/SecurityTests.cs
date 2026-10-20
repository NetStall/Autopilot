using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Settings;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using JobAutopilot.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Tests.Integration;

/// <summary>API hardening from task 03c part B (limits, CORS, hosts, headers, error details, import lock, rate limit).</summary>
public class SecurityTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private const string AllowedOrigin = "http://localhost:3000";

    private readonly ThrowingVacancySource _source = new();

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Cors:AllowedOrigins:0"] = AllowedOrigin,
        ["RateLimiting:ImportsPerMinute"] = "2",
    };

    private bool _failUiSettings;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
        services.AddSingleton<IOptions<UiOptions>>(_ => _failUiSettings
            ? new ThrowingOptions()
            : Options.Create(new UiOptions()));
    }

    private sealed class ThrowingOptions : IOptions<UiOptions>
    {
        public UiOptions Value => throw new InvalidOperationException("Sensitive internal detail: Password=hunter2");
    }

    [Theory]
    [InlineData("q", 201)]
    [InlineData("location", 201)]
    public async Task Oversized_text_filters_are_rejected(string parameter, int length)
    {
        var response = await Client.GetAsync($"/api/vacancies?{parameter}={new string('a', length)}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Text_filters_at_the_limit_are_accepted()
    {
        var response = await Client.GetAsync($"/api/vacancies?q={new string('a', 200)}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Too_many_employer_type_values_are_rejected()
    {
        var query = string.Join('&', Enumerable.Repeat("employerType=Direct", VacancyListQuery.MaxEmployerTypes + 1));

        var response = await Client.GetAsync($"/api/vacancies?{query}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("page=1000001")]
    [InlineData("page=-1")]
    [InlineData("pageSize=1000")]
    public async Task Paging_is_bounded(string query)
    {
        (await Client.GetAsync($"/api/vacancies?{query}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Request_bodies_over_the_limit_get_413()
    {
        var json = $$"""{ "title": "{{new string('x', (int)ApiSecurity.MaxRequestBodyBytes)}}", "company": "Firma" }""";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/vacancies", content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Cors_allows_the_configured_origin()
    {
        var response = await PreflightAsync(AllowedOrigin);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(AllowedOrigin);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost:3001")]
    [InlineData("null")]
    public async Task Cors_gives_other_origins_no_cors_headers(string origin)
    {
        var preflight = await PreflightAsync(origin);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/vacancies");
        request.Headers.Add("Origin", origin);
        var get = await Client.SendAsync(request, CancellationToken);

        preflight.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        get.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Cors_allows_only_the_methods_the_api_uses()
    {
        var response = await PreflightAsync(AllowedOrigin, "DELETE");

        // The middleware lists the policy's methods; the browser refuses everything else itself.
        // DELETE since step 05 (deleting an application); PUT since step 05b/06 (Suchprofil, documents).
        var allowed = response.Headers.TryGetValues("Access-Control-Allow-Methods", out var values)
            ? string.Join(',', values)
            : string.Empty;
        allowed.Split(',').Should().BeEquivalentTo("GET", "POST", "PUT", "PATCH", "DELETE");
    }

    [Fact]
    public async Task Preflight_for_saving_the_search_profile_with_put_succeeds()
    {
        // Regression: the Suchprofil page saves with PUT; without it in the policy the browser blocked the save.
        var response = await PreflightAsync(AllowedOrigin, "PUT");

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).Should().BeTrue();
        origins.Should().Equal(AllowedOrigin);
    }

    [Fact]
    public async Task Requests_for_a_foreign_host_name_are_rejected()
    {
        // DNS rebinding: a malicious site resolving its own name to 127.0.0.1 sends its host name.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/vacancies");
        request.Headers.Host = "evil.example";

        var response = await Client.SendAsync(request, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await Client.GetAsync("/api/vacancies", CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Unexpected_errors_return_problem_details_without_internals()
    {
        // GET /api/settings/ui reads IOptions<UiOptions>; a failing options source produces a 500.
        _failUiSettings = true;
        _ = Factory.Services; // build the host with the throwing options

        var response = await Client.GetAsync("/api/settings/ui", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        body.Should().NotContain("Sensitive").And.NotContain("hunter2").And.NotContain("InvalidOperationException")
            .And.NotContain(" at JobAutopilot", "no stack trace outside Development");
    }

    [Fact]
    public async Task Unexpected_errors_during_an_import_are_recorded_without_internals()
    {
        _source.Exception = new InvalidOperationException("Sensitive internal detail: Password=hunter2");

        var run = await RunImportAsync();

        run.Status.Should().Be(ImportRunStatus.Failed);
        run.Error.Should().Be("Unexpected error. Nothing was imported; see the API log.");
    }

    [Fact]
    public async Task A_second_import_while_one_is_active_gets_409()
    {
        await using (var db = Postgres.CreateDbContext())
        {
            db.ImportRuns.Add(ImportRun.Queue(ImportTrigger.Scheduled, 1, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(CancellationToken);
        }

        var blocked = await PostImportAsync();

        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        problem!.Title.Should().Be("An import is already running.");
    }

    [Fact]
    public async Task Database_allows_only_one_active_run()
    {
        await using var db = Postgres.CreateDbContext();
        db.ImportRuns.Add(ImportRun.Queue(ImportTrigger.Manual, 1, DateTimeOffset.UtcNow));
        db.ImportRuns.Add(ImportRun.Queue(ImportTrigger.Scheduled, 1, DateTimeOffset.UtcNow));

        var act = () => db.SaveChangesAsync(CancellationToken);

        (await act.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<DbUpdateException>(e => DbErrors.IsUniqueViolation(e));
    }

    [Fact]
    public async Task Run_fails_cleanly_when_another_process_holds_the_import_lock()
    {
        var importLock = Factory.Services.GetRequiredService<IImportLock>();
        await using var otherProcess = await importLock.TryAcquireAsync(CancellationToken);

        var run = await RunImportAsync();

        run.Status.Should().Be(ImportRunStatus.Failed);
        run.Error.Should().Be("Another import is running against the same database.");
    }

    [Fact]
    public async Task Import_endpoint_is_rate_limited()
    {
        await RunImportAsync();
        await RunImportAsync();

        var third = await PostImportAsync();

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.Contains("Retry-After").Should().BeTrue();
        var problem = await third.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        problem!.Title.Should().Be("Too many import requests.");
        (await Client.GetAsync("/api/vacancies", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "only imports are limited");
    }

    private async Task<HttpResponseMessage> PreflightAsync(string origin, string method = "GET")
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/vacancies");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        return await Client.SendAsync(request, CancellationToken);
    }

    private sealed class ThrowingVacancySource : IVacancySource
    {
        public Exception? Exception { get; set; }

        public VacancySource Source => VacancySource.Arbeitsagentur;

        public async IAsyncEnumerable<VacancyCandidate> SearchAsync(
            VacancySearch search,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Exception is not null)
            {
                throw Exception;
            }

            yield break;
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Import;
using Microsoft.Extensions.DependencyInjection;

namespace JobAutopilot.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Integration";
}

/// <summary>Gives every test an empty database, a fresh API instance and an HTTP client for it.</summary>
[Collection(IntegrationTestGroup.Name)]
public abstract class IntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected ApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    protected static JsonSerializerOptions ApiJson { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><c>POST /api/imports</c> with an optional JSON body.</summary>
    protected async Task<HttpResponseMessage> PostImportAsync(string? json = null)
    {
        using var content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PostAsync("/api/imports", content, CancellationToken);
    }

    /// <summary>Starts an import, expects 202 and polls <c>GET /api/imports/{id}</c> until it has finished.</summary>
    protected async Task<ImportRunDto> RunImportAsync(string? json = null)
    {
        var response = await PostImportAsync(json);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = (await response.Content.ReadFromJsonAsync<ImportStartedDto>(ApiJson, CancellationToken))!;
        response.Headers.Location!.ToString().Should().Be($"/api/imports/{started.ImportId}");
        return await WaitForImportAsync(started.ImportId);
    }

    protected async Task<ImportRunDto> WaitForImportAsync(Guid importId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            var run = await GetImportAsync(importId);
            if (run.Status is not (ImportRunStatus.Queued or ImportRunStatus.Running))
            {
                return run;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Import {importId} still {run.Status}.");
            }

            await Task.Delay(50, CancellationToken);
        }
    }

    protected async Task<ImportRunDto> GetImportAsync(Guid importId) =>
        (await Client.GetFromJsonAsync<ImportRunDto>($"/api/imports/{importId}", ApiJson, CancellationToken))!;

    public async ValueTask InitializeAsync()
    {
        Postgres.EnsureAvailable();
        await Postgres.ResetAsync(CancellationToken);

        Factory = new ApiFactory(Postgres, ConfigureServices, Settings);
        Client = Factory.CreateClient();
    }

    /// <summary>Override to set configuration values (e.g. CORS origins) for the tests of one class.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();

    /// <summary>Override to replace services (e.g. external sources) for the tests of one class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

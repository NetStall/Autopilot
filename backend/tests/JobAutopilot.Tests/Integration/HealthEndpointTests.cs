using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace JobAutopilot.Tests.Integration;

public class HealthEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Ready_returns_ok_when_the_database_is_reachable()
    {
        var response = await Client.GetAsync("/health/ready", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        body.GetProperty("status").GetString().Should().Be("ok");
        body.GetProperty("checks").GetProperty("database").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Live_checks_only_the_process()
    {
        var response = await Client.GetAsync("/health/live", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        body.GetProperty("status").GetString().Should().Be("ok");
        body.GetProperty("checks").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Old_health_path_is_gone()
    {
        var response = await Client.GetAsync("/health", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

using System.Text.Json;
using JobAutopilot.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobAutopilot.Api.Features.Health;

public static class HealthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>GET /health/live</c>: the process is up (no dependencies checked) — for restart decisions.
    /// <c>GET /health/ready</c>: the API can serve requests, i.e. the database is reachable — for traffic and
    /// <c>depends_on</c>. Both return 200 with <c>status = ok</c>, otherwise 503 with <c>status = unhealthy</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(DependencyInjection.ReadyTag),
            ResponseWriter = WriteResponseAsync,
        });
        return app;
    }

    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = report.Status == HealthStatus.Healthy ? "ok" : "unhealthy",
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}

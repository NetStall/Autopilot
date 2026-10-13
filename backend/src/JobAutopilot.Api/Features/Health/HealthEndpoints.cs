using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobAutopilot.Api.Features.Health;

public static class HealthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>GET /health</c>: 200 with <c>status = ok</c> when every check (including the database)
    /// passes, otherwise 503 with <c>status = unhealthy</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteResponseAsync });
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

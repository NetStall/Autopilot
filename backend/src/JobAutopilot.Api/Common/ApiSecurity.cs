using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace JobAutopilot.Api.Common;

/// <summary>CORS, rate limiting, request size limit and security headers. See docs/SECURITY.md.</summary>
public static class ApiSecurity
{
    public const string FrontendCorsPolicy = "Frontend";
    public const string ImportRateLimitPolicy = "imports";

    /// <summary>No endpoint accepts uploads; the biggest legitimate body (100 ids) is about 4 KB.</summary>
    public const long MaxRequestBodyBytes = 64 * 1024;

    public static WebApplicationBuilder AddApiSecurity(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        ValidateOrigins(origins);
        builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
            .WithOrigins(origins)
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Patch)
            .WithHeaders("Content-Type")));

        var importsPerMinute = builder.Configuration.GetValue("RateLimiting:ImportsPerMinute", 6);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(ImportRateLimitPolicy, limiter =>
            {
                limiter.PermitLimit = importsPerMinute;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await TypedResults.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many import requests.",
                    detail: $"At most {importsPerMinute} imports per minute. Try again later.").ExecuteAsync(http);
            };
        });

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
        return builder;
    }

    /// <summary>
    /// Rejects declared oversized bodies with 413 ProblemDetails before any endpoint runs (Kestrel additionally
    /// enforces the limit for bodies without Content-Length), and sets security headers on every response.
    /// </summary>
    public static IApplicationBuilder UseApiSecurity(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        // JSON API: nothing to load, nothing to embed.
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers.CacheControl = "no-store";

        if (context.Request.ContentLength > MaxRequestBodyBytes)
        {
            await TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Request body too large.",
                detail: $"At most {MaxRequestBodyBytes / 1024} KB are accepted.").ExecuteAsync(context);
            return;
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
        }

        await next(context);
    });

    /// <summary>Only absolute http(s) origins; a wildcard would let any website read the API.</summary>
    internal static void ValidateOrigins(IEnumerable<string> origins)
    {
        foreach (var origin in origins)
        {
            var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && uri.AbsolutePath == "/"
                && !origin.Contains('*', StringComparison.Ordinal);
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins contains '{origin}'. Only absolute http(s) origins without wildcards are allowed.");
            }
        }
    }
}

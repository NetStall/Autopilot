using System.Net.Http.Headers;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Infrastructure.Arbeitsagentur;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>Health checks with this tag decide readiness (<c>/health/ready</c>).</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString, npgsql => npgsql
            // Transient failures (DB restart, network blip) are retried; nothing here uses explicit transactions.
            .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null)
            .CommandTimeout(30)));
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: [ReadyTag]);

        services.AddSingleton<IImportLock>(new PostgresImportLock(connectionString));

        services.AddScoped<IVacancyStore, EfVacancyStore>();
        services.AddScoped<VacancyImportService>();
        services.AddScoped<VacancyDetailsService>();
        services.AddScoped<VacancyRescoreService>();

        services.AddScoring(configuration);
        services.AddArbeitsagentur(configuration);

        return services;
    }

    /// <summary>Relevance, remote and employer rules from configuration; invalid rules stop the start.</summary>
    private static void AddScoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RelevanceOptions>()
            .Bind(configuration.GetSection(RelevanceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RelevanceOptions>, RelevanceOptionsValidator>();
        services.AddOptions<RemoteOptions>().Bind(configuration.GetSection(RemoteOptions.SectionName));

        services.AddSingleton(sp => new RelevanceScorer(sp.GetRequiredService<IOptions<RelevanceOptions>>().Value));
        services.AddSingleton(sp => new RemoteClassifier(sp.GetRequiredService<IOptions<RemoteOptions>>().Value));
        services.AddSingleton<VacancyEnricher>();
    }

    private static void AddArbeitsagentur(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArbeitsagenturOptions>()
            .Bind(configuration.GetSection(ArbeitsagenturOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ArbeitsagenturOptions>, ArbeitsagenturOptionsValidator>();

        services
            .AddHttpClient<ArbeitsagenturClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ArbeitsagenturOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(ArbeitsagenturClient.UserAgent);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            // Retries with exponential backoff on 5xx, 408, 429 and timeouts; circuit breaker; total timeout.
            .AddStandardResilienceHandler();

        services.AddTransient<IVacancySource>(sp => sp.GetRequiredService<ArbeitsagenturClient>());
        services.AddTransient<IVacancyDetailsSource>(sp => sp.GetRequiredService<ArbeitsagenturClient>());
    }
}

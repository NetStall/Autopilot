using System.Net.Http.Headers;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Infrastructure.Arbeitsagentur;
using JobAutopilot.Infrastructure.Himalayas;
using JobAutopilot.Infrastructure.Persistence;
using JobAutopilot.Infrastructure.Profile;
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
        services.AddScoped<Applications.TrackerImporter>();

        services.AddScoring(configuration);
        services.AddArbeitsagentur(configuration);
        services.AddHimalayas(configuration);

        return services;
    }

    private static void AddHimalayas(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HimalayasOptions>()
            .Bind(configuration.GetSection(HimalayasOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HimalayasOptions>, HimalayasOptionsValidator>();

        services
            .AddHttpClient<HimalayasClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<HimalayasOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(ArbeitsagenturClient.UserAgent);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }
            })
            // Retries 5xx, 408 and 429 (respecting Retry-After) with backoff; circuit breaker; total timeout.
            .AddStandardResilienceHandler();

        services.AddTransient<IVacancySource>(sp => sp.GetRequiredService<HimalayasClient>());
    }

    /// <summary>Relevance, remote and employer rules from configuration; invalid relevance rules stop the start.</summary>
    private static void AddScoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RelevanceOptions>()
            .Bind(configuration.GetSection(RelevanceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RelevanceOptions>, RelevanceOptionsValidator>();
        services.AddOptions<RemoteOptions>().Bind(configuration.GetSection(RemoteOptions.SectionName));
        services.AddOptions<EmployerDetectionOptions>().Bind(configuration.GetSection(EmployerDetectionOptions.SectionName));

        services.AddSingleton<IRelevanceScorerProvider>(sp => new ProfileRelevanceScorerProvider(
            sp.GetRequiredService<IOptions<RelevanceOptions>>().Value,
            sp.GetRequiredService<ICandidateProfileSource>()));
        services.AddSingleton(sp => new RemoteClassifier(sp.GetRequiredService<IOptions<RemoteOptions>>().Value));
        services.AddSingleton(sp => new EmployerClassifier(sp.GetRequiredService<IOptions<EmployerDetectionOptions>>().Value));
        services.AddOptions<FitOptions>().Bind(configuration.GetSection(FitOptions.SectionName));
        services.AddSingleton(sp => new FitAnalyzer(sp.GetRequiredService<IOptions<FitOptions>>().Value));
        services.AddSingleton<VacancyFitService>();
        services.AddSingleton<VacancyEnricher>();

        // The API replaces this with the file-based source (it knows the content root).
        services.TryAddSingleton<ICandidateProfileSource, NoCandidateProfileSource>();
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

using System.Net.Http.Headers;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Infrastructure.Arbeitsagentur;
using JobAutopilot.Infrastructure.Claude;
using JobAutopilot.Infrastructure.Documents;
using JobAutopilot.Infrastructure.Himalayas;
using JobAutopilot.Infrastructure.Persistence;
using JobAutopilot.Infrastructure.Profile;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure;

public static partial class DependencyInjection
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
        services.AddDocuments(configuration);

        return services;
    }

    /// <summary>Letter generation (Claude), PDF rendering and the documents workflow (step 06, ADR 0010).</summary>
    private static void AddDocuments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ClaudeOptions>, ClaudeOptionsValidator>();

        // No resilience handler here: the SDK retries 429/5xx itself (Claude:MaxRetries) and applies Claude:TimeoutSeconds
        // per attempt; the HttpClient's own timeout would cut retries short.
        services.AddHttpClient<ClaudeLlmClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddTransient<Core.Llm.ILlmClient>(sp => sp.GetRequiredService<ClaudeLlmClient>());

        // Local model (step 07a), off unless Llm:Providers:Ollama:Enabled; retries and per-attempt timeout in the client.
        services.AddOptions<Llm.OllamaOptions>().Bind(configuration.GetSection(Llm.OllamaOptions.SectionName));
        services.AddHttpClient<Llm.OllamaLlmClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddTransient<Core.Llm.ILlmClient>(sp => sp.GetRequiredService<Llm.OllamaLlmClient>());
        services.AddOptions<Llm.VacancyExtractionOptions>().Bind(configuration.GetSection(Llm.VacancyExtractionOptions.SectionName));
        services.AddScoped<Llm.VacancyExtractionService>();
        services.AddScoped<Llm.VacancyFitScoreService>();
        services.AddScoped<Profile.ProfileTranslationService>();

        // Which provider and model a task uses (Llm:Tasks:Letter = claude:<model>); letter code only sees the router.
        services.AddOptions<Llm.LlmOptions>().Bind(configuration.GetSection(Llm.LlmOptions.SectionName));
        services.AddTransient<Core.Llm.ILlmRouter, Llm.LlmRouter>();
        services.AddTransient<ILetterGenerator, LetterGenerator>();

        services.AddSingleton<IDocumentRenderer, QuestPdfDocumentRenderer>();
        services.AddOptions<DocumentOptions>().Bind(configuration.GetSection(DocumentOptions.SectionName));
        // The API replaces this with a path resolved against its content root.
        services.TryAddSingleton(sp => new DocumentFileStore(sp.GetRequiredService<IOptions<DocumentOptions>>().Value.OutputPath));
        services.AddScoped<ApplicationDocumentsService>();
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
            sp.GetRequiredService<ICandidateProfileSource>(),
            sp.GetService<ISearchProfileSource>()));
        services.AddSingleton(sp => new RemoteClassifier(sp.GetRequiredService<IOptions<RemoteOptions>>().Value));
        services.AddSingleton(sp => new EmployerClassifier(sp.GetRequiredService<IOptions<EmployerDetectionOptions>>().Value));
        services.AddOptions<FitOptions>().Bind(configuration.GetSection(FitOptions.SectionName));
        services.AddSingleton(sp => new FitAnalyzer(sp.GetRequiredService<IOptions<FitOptions>>().Value));
        services.AddSingleton(sp => new VacancyFitService(
            sp.GetRequiredService<FitAnalyzer>(),
            sp.GetRequiredService<ICandidateProfileSource>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ISearchProfileSource>()));
        services.AddSingleton(sp =>
        {
            // Field and text disagree about home office (07b): logged with the vacancy id only.
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<VacancyEnricher>();
            return new VacancyEnricher(
                sp.GetRequiredService<IRelevanceScorerProvider>(),
                sp.GetRequiredService<RemoteClassifier>(),
                sp.GetRequiredService<EmployerClassifier>(),
                sp.GetRequiredService<VacancyFitService>(),
                (id, source) => RemoteConflictLog(logger, id, source));
        });

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Vacancy {VacancyId}: home office field and text disagree, {Source} used")]
    private static partial void RemoteConflictLog(ILogger logger, Guid vacancyId, RemoteSource source);
}

using System.Net.Http.Headers;
using JobAutopilot.Core.Import;
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

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        services.AddScoped<IVacancyStore, EfVacancyStore>();
        services.AddScoped<VacancyImportService>();

        services.AddArbeitsagentur(configuration);

        return services;
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
    }
}

using JobAutopilot.Core.Import;
using JobAutopilot.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>Runs the real API in memory against the fixture's database and a controllable clock.</summary>
public sealed class ApiFactory(
    PostgresFixture postgres,
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    public TestTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public FakeDetailsSource DetailsSource { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" instead of "Development": the fixture applies migrations itself.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);

        // Deterministic defaults; a test can override them with its settings.
        builder.UseSetting("Relevance:RescoreOnStartup", "false");
        builder.UseSetting("Import:Details:DelayBetweenRequestsMs", "0");
        builder.UseSetting("Import:LockWaitSeconds", "3");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Never a real job source (Arbeitsagentur, Himalayas); tests that import register their own fakes.
            services.RemoveAll<IVacancySource>();
            services.AddSingleton<IVacancySource>(new FakeVacancySource());
            services.RemoveAll<IVacancyDetailsSource>();
            services.AddSingleton<IVacancyDetailsSource>(DetailsSource);
            configureServices?.Invoke(services);
        });
    }
}

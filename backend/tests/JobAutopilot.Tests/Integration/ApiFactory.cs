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

    /// <summary>The complete search list (configuration arrays merge by index).</summary>
    private static readonly Dictionary<string, string> TestSearches = new()
    {
        ["Import:Searches:0:Name"] = "region-softwareentwickler",
        ["Import:Searches:0:Keyword"] = "Softwareentwickler",
        ["Import:Searches:0:Location"] = "Talstadt",
        ["Import:Searches:0:RadiusKm"] = "70",
        ["Import:Searches:0:Source"] = "Arbeitsagentur",
        ["Import:Searches:0:RemoteOnly"] = "false",
        ["Import:Searches:1:Name"] = "region-csharp",
        ["Import:Searches:1:Keyword"] = "Softwareentwickler C#",
        ["Import:Searches:1:Location"] = "Talstadt",
        ["Import:Searches:1:RadiusKm"] = "70",
        ["Import:Searches:1:Source"] = "Arbeitsagentur",
        ["Import:Searches:1:RemoteOnly"] = "false",
        ["Import:Searches:2:Name"] = "region-csharp-dotnet",
        ["Import:Searches:2:Keyword"] = "C# .NET",
        ["Import:Searches:2:Location"] = "Talstadt",
        ["Import:Searches:2:RadiusKm"] = "70",
        ["Import:Searches:2:Source"] = "Arbeitsagentur",
        ["Import:Searches:2:RemoteOnly"] = "false",
        ["Import:Searches:3:Name"] = "region-dotnet",
        ["Import:Searches:3:Keyword"] = ".NET Entwickler",
        ["Import:Searches:3:Location"] = "Talstadt",
        ["Import:Searches:3:RadiusKm"] = "70",
        ["Import:Searches:3:Source"] = "Arbeitsagentur",
        ["Import:Searches:3:RemoteOnly"] = "false",
        ["Import:Searches:4:Name"] = "region-fullstack",
        ["Import:Searches:4:Keyword"] = "Full Stack",
        ["Import:Searches:4:Location"] = "Talstadt",
        ["Import:Searches:4:RadiusKm"] = "70",
        ["Import:Searches:4:Source"] = "Arbeitsagentur",
        ["Import:Searches:4:RemoteOnly"] = "false",
        ["Import:Searches:5:Name"] = "region-fullstack-alt",
        ["Import:Searches:5:Keyword"] = "Fullstack",
        ["Import:Searches:5:Location"] = "Talstadt",
        ["Import:Searches:5:RadiusKm"] = "70",
        ["Import:Searches:5:Source"] = "Arbeitsagentur",
        ["Import:Searches:5:RemoteOnly"] = "false",
        ["Import:Searches:6:Name"] = "region-anwendungsentw",
        ["Import:Searches:6:Keyword"] = "Anwendungsentwickler",
        ["Import:Searches:6:Location"] = "Talstadt",
        ["Import:Searches:6:RadiusKm"] = "70",
        ["Import:Searches:6:Source"] = "Arbeitsagentur",
        ["Import:Searches:6:RemoteOnly"] = "false",
        ["Import:Searches:7:Name"] = "region-backend",
        ["Import:Searches:7:Keyword"] = "Backend",
        ["Import:Searches:7:Location"] = "Talstadt",
        ["Import:Searches:7:RadiusKm"] = "70",
        ["Import:Searches:7:Source"] = "Arbeitsagentur",
        ["Import:Searches:7:RemoteOnly"] = "false",
        ["Import:Searches:8:Name"] = "region-vue",
        ["Import:Searches:8:Keyword"] = "Vue",
        ["Import:Searches:8:Location"] = "Talstadt",
        ["Import:Searches:8:RadiusKm"] = "70",
        ["Import:Searches:8:Source"] = "Arbeitsagentur",
        ["Import:Searches:8:RemoteOnly"] = "false",
        ["Import:Searches:9:Name"] = "remote-csharp",
        ["Import:Searches:9:Keyword"] = "C#",
        ["Import:Searches:9:RemoteOnly"] = "true",
        ["Import:Searches:9:Source"] = "Arbeitsagentur",
        ["Import:Searches:10:Name"] = "remote-dotnet",
        ["Import:Searches:10:Keyword"] = ".NET",
        ["Import:Searches:10:RemoteOnly"] = "true",
        ["Import:Searches:10:Source"] = "Arbeitsagentur",
        ["Import:Searches:11:Name"] = "remote-fullstack",
        ["Import:Searches:11:Keyword"] = "Full Stack",
        ["Import:Searches:11:RemoteOnly"] = "true",
        ["Import:Searches:11:Source"] = "Arbeitsagentur",
        ["Import:Searches:12:Name"] = "remote-vue",
        ["Import:Searches:12:Keyword"] = "Vue",
        ["Import:Searches:12:RemoteOnly"] = "true",
        ["Import:Searches:12:Source"] = "Arbeitsagentur",
        ["Import:Searches:13:Name"] = "remote-softwareentwickler",
        ["Import:Searches:13:Keyword"] = "Softwareentwickler",
        ["Import:Searches:13:RemoteOnly"] = "true",
        ["Import:Searches:13:Source"] = "Arbeitsagentur",
        ["Import:Searches:14:Name"] = "remote-backend",
        ["Import:Searches:14:Keyword"] = "Backend",
        ["Import:Searches:14:RemoteOnly"] = "true",
        ["Import:Searches:14:Source"] = "Arbeitsagentur",
        ["Import:Searches:15:Name"] = "himalayas-dotnet",
        ["Import:Searches:15:Keyword"] = ".NET",
        ["Import:Searches:15:Source"] = "Himalayas",
        ["Import:Searches:15:RemoteOnly"] = "true",
        ["Import:Searches:16:Name"] = "himalayas-csharp",
        ["Import:Searches:16:Keyword"] = "C#",
        ["Import:Searches:16:Source"] = "Himalayas",
        ["Import:Searches:16:RemoteOnly"] = "true",
        ["Import:Searches:17:Name"] = "himalayas-vue",
        ["Import:Searches:17:Keyword"] = "Vue",
        ["Import:Searches:17:Source"] = "Himalayas",
        ["Import:Searches:17:RemoteOnly"] = "true",
        ["Import:Searches:18:Name"] = "himalayas-fullstack",
        ["Import:Searches:18:Keyword"] = "Full Stack",
        ["Import:Searches:18:Source"] = "Himalayas",
        ["Import:Searches:18:RemoteOnly"] = "true",
        ["Import:Searches:19:Name"] = "himalayas-softwareentwickler",
        ["Import:Searches:19:Keyword"] = "Softwareentwickler",
        ["Import:Searches:19:Source"] = "Himalayas",
        ["Import:Searches:19:RemoteOnly"] = "true",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" instead of "Development": the fixture applies migrations itself.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);

        // Deterministic defaults; a test can override them with its settings.
        builder.UseSetting("Relevance:RescoreOnStartup", "false");
        builder.UseSetting("Import:Details:DelayBetweenRequestsMs", "0");
        builder.UseSetting("Import:LockWaitSeconds", "3");
        // The searches of an invented person (home „Talstadt"); the real ones live in appsettings.Local.json (08d).
        foreach (var (key, value) in TestSearches)
        {
            builder.UseSetting(key, value);
        }

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

            // Never a real local model: Ollama's HTTP client behaves as if nothing listens on the port.
            services.AddHttpClient<Infrastructure.Llm.OllamaLlmClient>().ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
            configureServices?.Invoke(services);
        });
    }
}

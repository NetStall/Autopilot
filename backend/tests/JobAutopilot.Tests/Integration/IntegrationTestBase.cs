using Microsoft.Extensions.DependencyInjection;

namespace JobAutopilot.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Integration";
}

/// <summary>Gives every test an empty database, a fresh API instance and an HTTP client for it.</summary>
[Collection(IntegrationTestGroup.Name)]
public abstract class IntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected ApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Postgres.EnsureAvailable();
        await Postgres.ResetAsync(CancellationToken);

        Factory = new ApiFactory(Postgres, ConfigureServices);
        Client = Factory.CreateClient();
    }

    /// <summary>Override to replace services (e.g. external sources) for the tests of one class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

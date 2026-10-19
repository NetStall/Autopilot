using DotNet.Testcontainers.Builders;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace JobAutopilot.Tests.Integration;

/// <summary>
/// Provides a throw-away PostgreSQL database for the integration tests.
/// <list type="bullet">
/// <item>If <c>JOBAUTOPILOT_TEST_POSTGRES</c> is set, a temporary database is created on that server.</item>
/// <item>Otherwise a PostgreSQL container is started with Testcontainers.</item>
/// <item>If neither works (no Docker), the tests are skipped locally but fail in CI.</item>
/// </list>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ServerConnectionVariable = "JOBAUTOPILOT_TEST_POSTGRES";

    private PostgreSqlContainer? _container;
    private string? _unavailableReason;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(ServerConnectionVariable);

        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            try
            {
                _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                await _container.StartAsync();
                serverConnectionString = _container.GetConnectionString();
            }
            catch (Exception ex) when (ex is DockerUnavailableException || ex.InnerException is DockerUnavailableException)
            {
                _unavailableReason =
                    $"Docker is not available and {ServerConnectionVariable} is not set ({ex.Message})";
                return;
            }
        }

        // Always use a dedicated database so the tests never touch real data.
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = $"jobautopilot_tests_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_unavailableReason is null && ConnectionString.Length > 0)
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Skips the calling test when no database is available (outside CI).</summary>
    public void EnsureAvailable()
    {
        if (_unavailableReason is null)
        {
            return;
        }

        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        if (isCi)
        {
            throw new InvalidOperationException(_unavailableReason);
        }

        Assert.Skip(_unavailableReason);
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await db.Applications.ExecuteDeleteAsync(cancellationToken);
        await db.Vacancies.ExecuteDeleteAsync(cancellationToken);
        await db.ImportRuns.ExecuteDeleteAsync(cancellationToken);
    }
}

using JobAutopilot.Core.Import;
using Npgsql;

namespace JobAutopilot.Infrastructure.Persistence;

/// <summary>
/// <see cref="IImportLock"/> on a PostgreSQL session-level advisory lock. It works across processes (dev.ps1 and
/// docker compose against the same database, several API instances later on Azure), and PostgreSQL releases it
/// automatically if the holding connection dies, so a crashed import can never block the next one.
/// </summary>
internal sealed class PostgresImportLock(string connectionString) : IImportLock
{
    /// <summary>Arbitrary, fixed application-wide key ("JobAutoI" as ASCII).</summary>
    internal const long LockKey = 0x4A6F624175746F49;

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Handle(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Handle(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                // Closing the session releases the lock in any case.
                await connection.DisposeAsync();
            }
        }
    }
}

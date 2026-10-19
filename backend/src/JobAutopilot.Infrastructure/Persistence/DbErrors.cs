using Npgsql;

namespace JobAutopilot.Infrastructure.Persistence;

public static class DbErrors
{
    /// <summary>PostgreSQL 23505 — a unique index (e.g. "only one active import") rejected the write.</summary>
    public static bool IsUniqueViolation(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

namespace JobAutopilot.Infrastructure.Persistence;

/// <summary>Configuration section <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Apply pending EF migrations when the API starts. Off by default; switched on in
    /// <c>appsettings.Development.json</c> and in docker-compose. Independent of the environment name.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}

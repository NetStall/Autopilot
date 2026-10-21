using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();

    public DbSet<Core.Documents.ApplicationDraft> ApplicationDrafts => Set<Core.Documents.ApplicationDraft>();

    public DbSet<Core.Extraction.VacancyExtraction> VacancyExtractions => Set<Core.Extraction.VacancyExtraction>();

    public DbSet<Core.Extraction.GoldLabel> GoldLabels => Set<Core.Extraction.GoldLabel>();

    public DbSet<Core.Documents.ApplicationDraftRevision> ApplicationDraftRevisions => Set<Core.Documents.ApplicationDraftRevision>();

    public DbSet<SearchProfiles.SearchProfileRecord> SearchProfiles => Set<SearchProfiles.SearchProfileRecord>();

    public DbSet<SearchProfiles.SearchProfileRevision> SearchProfileRevisions => Set<SearchProfiles.SearchProfileRevision>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}

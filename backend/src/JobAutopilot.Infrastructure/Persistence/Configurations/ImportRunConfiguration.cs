using JobAutopilot.Core.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAutopilot.Infrastructure.Persistence.Configurations;

internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    private const int EnumMaxLength = 32;

    public void Configure(EntityTypeBuilder<ImportRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(r => r.CurrentSearch).HasMaxLength(VacancySearchRules.NameMaxLength);
        builder.Property(r => r.Error).HasMaxLength(1000);

        // Per-search counts as one jsonb column; they are only ever read together with the run.
        builder.OwnsMany(r => r.Searches, searches =>
        {
            searches.ToJson();
            searches.Property(s => s.Source).HasConversion<string>();
        });

        // DB guard: at most one queued or running import at any time (insert of a second one fails).
        builder.HasIndex(r => r.IsActive)
            .IsUnique()
            .HasFilter("\"IsActive\"")
            .HasDatabaseName("IX_ImportRuns_OnlyOneActive");

        builder.HasIndex(r => r.RequestedAt);
    }
}

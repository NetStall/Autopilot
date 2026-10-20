using JobAutopilot.Infrastructure.SearchProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAutopilot.Infrastructure.Persistence.Configurations;

internal sealed class SearchProfileConfiguration : IEntityTypeConfiguration<SearchProfileRecord>
{
    public void Configure(EntityTypeBuilder<SearchProfileRecord> builder)
    {
        builder.ToTable("SearchProfiles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Document).HasColumnType("jsonb");

        // Optimistic concurrency: a save from a stale version (another tab, another process) is rejected.
        builder.Property(r => r.Version).IsConcurrencyToken();
    }
}

internal sealed class SearchProfileRevisionConfiguration : IEntityTypeConfiguration<SearchProfileRevision>
{
    public void Configure(EntityTypeBuilder<SearchProfileRevision> builder)
    {
        builder.ToTable("SearchProfileRevisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Document).HasColumnType("jsonb");
        builder.Property(r => r.Summary).HasMaxLength(4000);
        builder.HasIndex(r => r.Version).IsUnique();
    }
}

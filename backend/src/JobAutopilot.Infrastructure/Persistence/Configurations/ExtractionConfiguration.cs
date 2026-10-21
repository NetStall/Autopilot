using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAutopilot.Infrastructure.Persistence.Configurations;

internal sealed class VacancyExtractionConfiguration : IEntityTypeConfiguration<VacancyExtraction>
{
    public void Configure(EntityTypeBuilder<VacancyExtraction> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.DescriptionHash).HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.Model).HasMaxLength(VacancyExtraction.ModelMaxLength);
        builder.Property(e => e.PromptVersion).HasMaxLength(VacancyExtraction.PromptVersionMaxLength);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.ResultJson).HasColumnType("jsonb");
        builder.Property(e => e.Error).HasMaxLength(VacancyExtraction.ErrorMaxLength);

        // The cache key: the same text with the same model and prompt is analysed once.
        builder.HasIndex(e => new { e.DescriptionHash, e.Model, e.PromptVersion }).IsUnique();
    }
}

internal sealed class GoldLabelConfiguration : IEntityTypeConfiguration<GoldLabel>
{
    public void Configure(EntityTypeBuilder<GoldLabel> builder)
    {
        builder.HasKey(l => l.VacancyId);
        builder.HasOne<Vacancy>().WithOne().HasForeignKey<GoldLabel>(l => l.VacancyId).OnDelete(DeleteBehavior.Cascade);
    }
}

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

internal sealed class VacancyFitScoreConfiguration : IEntityTypeConfiguration<VacancyFitScore>
{
    public void Configure(EntityTypeBuilder<VacancyFitScore> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DescriptionHash).HasMaxLength(64).IsFixedLength();
        builder.Property(s => s.ProfileHash).HasMaxLength(16).IsFixedLength();
        builder.Property(s => s.Model).HasMaxLength(VacancyExtraction.ModelMaxLength);
        builder.Property(s => s.PromptVersion).HasMaxLength(VacancyExtraction.PromptVersionMaxLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ResultJson).HasColumnType("jsonb");
        builder.Property(s => s.Error).HasMaxLength(VacancyExtraction.ErrorMaxLength);
        builder.HasIndex(s => new { s.DescriptionHash, s.ProfileHash, s.Model, s.PromptVersion }).IsUnique();
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

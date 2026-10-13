using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAutopilot.Infrastructure.Persistence.Configurations;

internal sealed class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    private const int EnumMaxLength = 32;

    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Source)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength);

        builder.Property(v => v.ExternalId).HasMaxLength(VacancyLimits.ExternalIdMaxLength);
        builder.Property(v => v.Title).HasMaxLength(VacancyLimits.TitleMaxLength);
        builder.Property(v => v.Company).HasMaxLength(VacancyLimits.CompanyMaxLength);
        builder.Property(v => v.Location).HasMaxLength(VacancyLimits.LocationMaxLength);
        builder.Property(v => v.Url).HasMaxLength(VacancyLimits.UrlMaxLength);
        builder.Property(v => v.Description).HasMaxLength(VacancyLimits.DescriptionMaxLength);

        builder.Property(v => v.EmployerType)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength);

        builder.HasIndex(v => new { v.Source, v.ExternalId }).IsUnique();
        builder.HasIndex(v => v.FetchedAt);
    }
}

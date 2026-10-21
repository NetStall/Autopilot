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

        // PostgreSQL text[]; Core only sees a List<string>.
        // Default '{}' so the column can be added to tables that already have rows.
        builder.Property(v => v.MatchedSearches)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.HasIndex(v => new { v.Source, v.ExternalId }).IsUnique();
        builder.Property(v => v.Interest)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength)
            .HasDefaultValue(VacancyInterest.None)
            .HasSentinel(VacancyInterest.None);

        builder.Property(v => v.RemoteLevel)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength)
            .HasDefaultValue(RemoteLevel.Unknown)
            .HasSentinel(RemoteLevel.Unknown);

        builder.Property(v => v.RemoteHint)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength)
            .HasDefaultValue(RemoteLevel.Unknown)
            .HasSentinel(RemoteLevel.Unknown);

        builder.Property(v => v.RelevanceLevel)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength);

        builder.Property(v => v.RequiredGerman)
            .HasConversion<string>()
            .HasMaxLength(EnumMaxLength)
            .HasDefaultValue(RequiredGerman.Unknown)
            .HasSentinel(RequiredGerman.Unknown);

        builder.Property(v => v.Unreachable).HasDefaultValue(false);
        builder.Property(v => v.DescriptionIsManual).HasDefaultValue(false);
        builder.Property(v => v.ExtractionJson).HasColumnType("jsonb");
        builder.Property(v => v.ExtractionKey).HasMaxLength(250);

        builder.Property(v => v.RelevanceReasons)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(v => v.DuplicateKey).HasMaxLength(DuplicateKeys.MaxLength);

        builder.HasIndex(v => v.DuplicateKey);
        builder.HasIndex(v => v.RemoteLevel);
        builder.HasIndex(v => v.RelevanceLevel);
        builder.HasIndex(v => v.RankScore);
        builder.HasIndex(v => v.RequiredGerman);
        builder.HasIndex(v => v.Interest);
        builder.HasIndex(v => v.FetchedAt);
    }
}

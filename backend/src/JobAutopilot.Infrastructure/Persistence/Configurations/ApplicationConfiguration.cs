using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAutopilot.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    private const int EnumMaxLength = 32;

    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Company).HasMaxLength(VacancyLimits.CompanyMaxLength);
        builder.Property(a => a.Position).HasMaxLength(VacancyLimits.TitleMaxLength);
        builder.Property(a => a.Location).HasMaxLength(VacancyLimits.LocationMaxLength);
        builder.Property(a => a.Channel).HasMaxLength(100);
        builder.Property(a => a.ContactName).HasMaxLength(200);
        builder.Property(a => a.ContactEmail).HasMaxLength(320);
        builder.Property(a => a.Url).HasMaxLength(VacancyLimits.UrlMaxLength);
        builder.Property(a => a.NextStep).HasMaxLength(2000);
        builder.Property(a => a.Notes).HasMaxLength(10_000);
        builder.Property(a => a.ExternalRef).HasMaxLength(100);
        builder.Property(a => a.DuplicateKey).HasMaxLength(DuplicateKeys.MaxLength);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(a => a.Documents).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]").IsRequired();
        builder.Ignore(a => a.FollowUpAt);

        // A deleted vacancy (e.g. an expired Himalayas job) must not delete the application, only the link.
        builder.HasOne<Vacancy>().WithMany().HasForeignKey(a => a.VacancyId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(a => a.Events).WithOne().HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Events).AutoInclude(false);

        builder.HasIndex(a => a.ExternalRef).IsUnique().HasFilter("\"ExternalRef\" IS NOT NULL");
        builder.HasIndex(a => a.VacancyId);
        builder.HasIndex(a => a.DuplicateKey);
        builder.HasIndex(a => a.Status);
    }
}

internal sealed class ApplicationEventConfiguration : IEntityTypeConfiguration<ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ApplicationEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.From).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.To).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.HasIndex(e => new { e.ApplicationId, e.At });
    }
}

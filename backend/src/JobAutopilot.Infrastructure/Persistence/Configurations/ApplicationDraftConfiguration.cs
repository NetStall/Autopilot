using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobAutopilot.Infrastructure.Persistence.Configurations;

/// <summary>Letter, CV options and warnings as jsonb documents (camelCase, enums as strings).</summary>
internal static class JsonColumn
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static ValueConverter<T, string> Converter<T>() => new(
        value => JsonSerializer.Serialize(value, Options),
        json => JsonSerializer.Deserialize<T>(json, Options)!);

    /// <summary>Compares by content, so changes inside the (immutable) records are always noticed.</summary>
    public static ValueComparer<T> Comparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);

    public static PropertyBuilder<T> AsJson<T>(this PropertyBuilder<T> property)
    {
        property.HasColumnType("jsonb").HasConversion(Converter<T>(), Comparer<T>()).IsRequired();
        return property;
    }
}

internal sealed class ApplicationDraftConfiguration : IEntityTypeConfiguration<ApplicationDraft>
{
    public void Configure(EntityTypeBuilder<ApplicationDraft> builder)
    {
        builder.ToTable("ApplicationDrafts");
        builder.Property(d => d.Package).HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<PackageTexts?>(), JsonColumn.Comparer<PackageTexts?>());
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        // One draft per application; deleting the application deletes its documents too.
        builder.HasOne<Application>().WithOne().HasForeignKey<ApplicationDraft>(d => d.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => d.ApplicationId).IsUnique();

        builder.Property(d => d.Letter).AsJson();
        builder.Property(d => d.CvOptions).AsJson();
        builder.Property(d => d.Warnings).AsJson();
        builder.Property(d => d.PromptVersion).HasMaxLength(50);
        builder.Property(d => d.Model).HasMaxLength(100);
        builder.Property(d => d.LetterFile).HasMaxLength(300);
        builder.Property(d => d.CvFile).HasMaxLength(300);

        // Optimistic concurrency: a save from another tab with an older version is rejected (409).
        builder.Property(d => d.Version).IsConcurrencyToken();

        builder.Ignore(d => d.Usage);
        builder.Ignore(d => d.HasOpenWarnings);

        builder.HasMany(d => d.Revisions).WithOne().HasForeignKey(r => r.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Revisions).AutoInclude(false);
    }
}

internal sealed class ApplicationDraftRevisionConfiguration : IEntityTypeConfiguration<ApplicationDraftRevision>
{
    public void Configure(EntityTypeBuilder<ApplicationDraftRevision> builder)
    {
        builder.ToTable("ApplicationDraftRevisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Letter).AsJson();
        builder.Property(r => r.CvOptions).AsJson();
        builder.Property(r => r.Model).HasMaxLength(100);
        builder.Property(r => r.PromptVersion).HasMaxLength(50);
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.Ignore(r => r.IsModelCall);
        builder.HasIndex(r => new { r.DraftId, r.Number }).IsUnique();

        // The daily call limit counts model calls since midnight.
        builder.HasIndex(r => new { r.Kind, r.CreatedAt });
    }
}

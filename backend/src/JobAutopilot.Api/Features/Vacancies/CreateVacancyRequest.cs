using System.ComponentModel.DataAnnotations;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Vacancies;

/// <summary>A vacancy entered by hand. Source and external id are set by the server.</summary>
public sealed class CreateVacancyRequest
{
    [Required]
    [MaxLength(VacancyLimits.TitleMaxLength)]
    public string Title { get; init; } = string.Empty;

    [Required]
    [MaxLength(VacancyLimits.CompanyMaxLength)]
    public string Company { get; init; } = string.Empty;

    [MaxLength(VacancyLimits.LocationMaxLength)]
    public string? Location { get; init; }

    [Url]
    [MaxLength(VacancyLimits.UrlMaxLength)]
    public string? Url { get; init; }

    [MaxLength(VacancyLimits.DescriptionMaxLength)]
    public string? Description { get; init; }

    public EmployerType EmployerType { get; init; } = EmployerType.Unknown;

    public DateTimeOffset? PublishedAt { get; init; }
}

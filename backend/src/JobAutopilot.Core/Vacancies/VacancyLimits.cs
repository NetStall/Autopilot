namespace JobAutopilot.Core.Vacancies;

/// <summary>Field length limits shared by API validation and the database schema.</summary>
public static class VacancyLimits
{
    public const int ExternalIdMaxLength = 100;
    public const int TitleMaxLength = 300;
    public const int CompanyMaxLength = 200;
    public const int LocationMaxLength = 200;
    public const int UrlMaxLength = 2048;
    public const int DescriptionMaxLength = 20_000;
}

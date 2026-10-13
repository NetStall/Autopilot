namespace JobAutopilot.Core.Vacancies;

/// <summary>Who is behind a vacancy. Direct employers are ranked above agencies.</summary>
public enum EmployerType
{
    Unknown,
    Direct,
    Agency,
    Temporary,
}

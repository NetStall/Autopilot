namespace JobAutopilot.Core.Import;

/// <summary>Thrown by an <see cref="IVacancySource"/> when the source cannot be reached or answers with garbage.</summary>
public sealed class VacancySourceUnavailableException : Exception
{
    public VacancySourceUnavailableException()
    {
    }

    public VacancySourceUnavailableException(string message)
        : base(message)
    {
    }

    public VacancySourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

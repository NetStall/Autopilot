namespace JobAutopilot.Core;

public static class TextLimits
{
    /// <summary>Cuts <paramref name="value"/> to at most <paramref name="maxLength"/> characters.</summary>
    public static string Truncate(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
    }
}

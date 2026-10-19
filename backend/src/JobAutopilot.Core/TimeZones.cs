namespace JobAutopilot.Core;

/// <summary>Finds time zones by IANA id on every platform (also on Windows with InvariantGlobalization, without ICU).</summary>
public static class TimeZones
{
    private static readonly Dictionary<string, string> WindowsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["Europe/Vienna"] = "W. Europe Standard Time",
        ["Europe/Zurich"] = "W. Europe Standard Time",
        ["UTC"] = "UTC",
    };

    public static TimeZoneInfo Berlin { get; } = Find("Europe/Berlin");

    public static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found)
            || (WindowsIds.TryGetValue(id, out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out found)))
        {
            timeZone = found;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    public static TimeZoneInfo Find(string id) =>
        TryFind(id, out var timeZone)
            ? timeZone
            : throw new InvalidOperationException($"Time zone '{id}' is not available on this system.");
}

namespace JobAutopilot.Core.Import;

/// <summary>
/// "Every day at <see cref="TimeOfDay"/> in <see cref="TimeZone"/>", DST-aware. On the spring-forward day a time
/// inside the gap (e.g. 02:30 in Berlin) moves to the first valid moment after the gap; on the fall-back day an
/// ambiguous time runs once, at its first occurrence.
/// </summary>
public sealed class DailySchedule(TimeOnly timeOfDay, TimeZoneInfo timeZone)
{
    public TimeOnly TimeOfDay { get; } = timeOfDay;

    public TimeZoneInfo TimeZone { get; } = timeZone ?? throw new ArgumentNullException(nameof(timeZone));

    /// <summary>The first scheduled moment strictly after <paramref name="now"/>.</summary>
    public DateTimeOffset NextAfter(DateTimeOffset now)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZone).DateTime);
        for (var day = localDate.AddDays(-1); ; day = day.AddDays(1))
        {
            var occurrence = OccurrenceOn(day);
            if (occurrence > now)
            {
                return occurrence;
            }
        }
    }

    /// <summary>The latest scheduled moment at or before <paramref name="now"/>.</summary>
    public DateTimeOffset LatestAtOrBefore(DateTimeOffset now)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZone).DateTime);
        for (var day = localDate.AddDays(1); ; day = day.AddDays(-1))
        {
            var occurrence = OccurrenceOn(day);
            if (occurrence <= now)
            {
                return occurrence;
            }
        }
    }

    /// <summary>
    /// A scheduled run was missed (the app was not running at that time) when the last import started before the
    /// latest scheduled moment. Used once at start-up to catch up.
    /// </summary>
    public bool IsDue(DateTimeOffset now, DateTimeOffset? lastImportRequestedAt) =>
        lastImportRequestedAt is null || lastImportRequestedAt < LatestAtOrBefore(now);

    internal DateTimeOffset OccurrenceOn(DateOnly day)
    {
        var local = day.ToDateTime(TimeOfDay, DateTimeKind.Unspecified);

        // Spring forward: the local time does not exist → first valid minute after the gap.
        while (TimeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        // Fall back: the local time exists twice → take the earlier one (larger offset).
        var offset = TimeZone.IsAmbiguousTime(local)
            ? TimeZone.GetAmbiguousTimeOffsets(local).Max()
            : TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}

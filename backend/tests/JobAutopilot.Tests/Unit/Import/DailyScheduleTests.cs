using FluentAssertions;
using JobAutopilot.Core;
using JobAutopilot.Core.Import;

namespace JobAutopilot.Tests.Unit.Import;

/// <summary>Daily 07:00 Europe/Berlin incl. daylight saving time (2026: 29 March and 25 October).</summary>
public class DailyScheduleTests
{
    private static readonly DailySchedule SevenAm = new(new TimeOnly(7, 0), TimeZones.Berlin);

    private static DateTimeOffset Berlin(int year, int month, int day, int hour, int minute, int offsetHours) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));

    [Theory]
    [InlineData("2026-10-19T04:59:00Z", "2026-10-19T05:00:00Z")] // 06:59 CEST → today 07:00
    [InlineData("2026-10-19T05:00:00Z", "2026-10-20T05:00:00Z")] // exactly 07:00 → tomorrow (strictly after)
    [InlineData("2026-10-19T12:00:00Z", "2026-10-20T05:00:00Z")]
    [InlineData("2026-10-19T22:30:00Z", "2026-10-20T05:00:00Z")] // 00:30 local next day
    [InlineData("2026-12-31T23:30:00Z", "2027-01-01T06:00:00Z")] // winter: 07:00 CET = 06:00 UTC, over new year
    public void Next_run_is_the_next_seven_o_clock_in_Berlin(string nowUtc, string expectedUtc)
    {
        SevenAm.NextAfter(DateTimeOffset.Parse(nowUtc)).Should().Be(DateTimeOffset.Parse(expectedUtc));
    }

    [Fact]
    public void Spring_forward_keeps_seven_o_clock_local_time()
    {
        // Sunday 29 March 2026: 02:00 CET → 03:00 CEST. Saturday 07:00 is UTC+1, Sunday 07:00 is UTC+2.
        var saturdayEvening = Berlin(2026, 3, 28, 20, 0, 1);

        var next = SevenAm.NextAfter(saturdayEvening);

        next.Should().Be(Berlin(2026, 3, 29, 7, 0, 2));
        next.UtcDateTime.Should().Be(new DateTime(2026, 3, 29, 5, 0, 0, DateTimeKind.Utc));
        (next - saturdayEvening).Should().Be(TimeSpan.FromHours(10), "the night is one hour shorter");
    }

    [Fact]
    public void Fall_back_keeps_seven_o_clock_local_time()
    {
        // Sunday 25 October 2026: 03:00 CEST → 02:00 CET.
        var saturdayEvening = Berlin(2026, 10, 24, 20, 0, 2);

        var next = SevenAm.NextAfter(saturdayEvening);

        next.Should().Be(Berlin(2026, 10, 25, 7, 0, 1));
        (next - saturdayEvening).Should().Be(TimeSpan.FromHours(12), "the night is one hour longer");
    }

    [Fact]
    public void A_time_inside_the_spring_gap_runs_right_after_the_gap()
    {
        var halfPastTwo = new DailySchedule(new TimeOnly(2, 30), TimeZones.Berlin);

        var next = halfPastTwo.NextAfter(Berlin(2026, 3, 28, 12, 0, 1));

        next.Should().Be(Berlin(2026, 3, 29, 3, 0, 2), "02:30 does not exist on that day");
    }

    [Fact]
    public void An_ambiguous_time_on_fall_back_runs_once_at_its_first_occurrence()
    {
        var halfPastTwo = new DailySchedule(new TimeOnly(2, 30), TimeZones.Berlin);

        var first = halfPastTwo.NextAfter(Berlin(2026, 10, 24, 12, 0, 2));
        var afterFirst = halfPastTwo.NextAfter(first);

        first.Should().Be(Berlin(2026, 10, 25, 2, 30, 2));
        afterFirst.Should().Be(Berlin(2026, 10, 26, 2, 30, 1), "the second 02:30 of that night is skipped");
    }

    [Fact]
    public void Latest_slot_at_or_before_now()
    {
        SevenAm.LatestAtOrBefore(DateTimeOffset.Parse("2026-10-19T05:00:00Z"))
            .Should().Be(DateTimeOffset.Parse("2026-10-19T05:00:00Z"));
        SevenAm.LatestAtOrBefore(DateTimeOffset.Parse("2026-10-19T04:59:00Z"))
            .Should().Be(DateTimeOffset.Parse("2026-10-18T05:00:00Z"));
    }

    [Theory]
    [InlineData("2026-10-19T09:00:00Z", null, true)]                    // never imported
    [InlineData("2026-10-19T09:00:00Z", "2026-10-18T20:00:00Z", true)]  // last import yesterday, today's 07:00 missed
    [InlineData("2026-10-19T09:00:00Z", "2026-10-19T05:00:00Z", false)] // today's run happened
    [InlineData("2026-10-19T09:00:00Z", "2026-10-19T08:30:00Z", false)] // manual import after 07:00 counts
    [InlineData("2026-10-19T04:00:00Z", "2026-10-18T06:00:00Z", false)] // before today's 07:00, yesterday's done
    public void Is_due_when_the_latest_slot_was_missed(string nowUtc, string? lastUtc, bool expected)
    {
        var last = lastUtc is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(lastUtc);

        SevenAm.IsDue(DateTimeOffset.Parse(nowUtc), last).Should().Be(expected);
    }
}

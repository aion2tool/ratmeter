using AionDpsMeter.Services.Services.Roster;
using Xunit;

public class RosterClockTests
{
    static DateTime U(int d, int h, int m = 0) => new(2026, 10, d, h, m, 0, DateTimeKind.Utc);

    [Fact] public void Day_starts_at_0700_utc() => Assert.Equal(U(22, 7), RosterClock.DayStart(U(22, 22)));
    [Fact] public void Before_0700_belongs_to_the_previous_day() => Assert.Equal(U(21, 7), RosterClock.DayStart(U(22, 6, 59)));
    [Fact] public void Week_starts_wednesday_0700_utc() => Assert.Equal(U(21, 7), RosterClock.WeekStart(U(25, 12)));   // Sun 25 -> Wed 21
    [Fact] public void Wednesday_before_0700_is_still_last_week() => Assert.Equal(U(14, 7), RosterClock.WeekStart(U(21, 6, 30)));
    [Fact] public void Wednesday_at_0700_is_the_new_week() => Assert.Equal(U(21, 7), RosterClock.WeekStart(U(21, 7)));
}

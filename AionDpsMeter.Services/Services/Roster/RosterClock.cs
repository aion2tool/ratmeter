namespace AionDpsMeter.Services.Services.Roster
{
    /// <summary>AION 2 Global resets: daily 07:00 UTC (16:00 KST), weekly Wednesday 07:00 UTC.</summary>
    public static class RosterClock
    {
        private static readonly TimeSpan ResetTime = TimeSpan.FromHours(7);

        public static DateTime DayStart(DateTime utcNow)
        {
            var d = utcNow.Date + ResetTime;
            return DateTime.SpecifyKind(utcNow >= d ? d : d.AddDays(-1), DateTimeKind.Utc);
        }

        public static DateTime WeekStart(DateTime utcNow)
        {
            var day = DayStart(utcNow);
            int back = ((int)day.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
            return day.AddDays(-back);
        }
    }
}

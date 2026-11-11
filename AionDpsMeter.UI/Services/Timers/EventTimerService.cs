using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Timers
{
    /// <summary>
    /// AION 2 event schedule (Spacetime Rift, sieges, field bosses, resets…) from shugo.gg's
    /// public timers.json. Cached on disk so timers still work offline; refreshed every few hours
    /// so corrections on shugo.gg reach the meter without an update.
    /// </summary>
    public sealed class EventTimerService
    {
        private const string SourceUrl = "https://shugo.gg/timers.json";
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);
        private static readonly HttpClient Http = CreateClient();

        private readonly ILogger<EventTimerService> logger;
        private readonly string cachePath;
        private ScheduleData? data;
        private DateTime lastRefresh = DateTime.MinValue;
        private bool refreshing;

        public EventTimerService(ILogger<EventTimerService> logger)
        {
            this.logger = logger;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter");
            Directory.CreateDirectory(dir);
            cachePath = Path.Combine(dir, "timers.json");
            try { if (File.Exists(cachePath)) data = Parse(File.ReadAllText(cachePath)); }
            catch (Exception ex) { logger.LogWarning(ex, "Cached timers.json unreadable"); }
        }

        public string? UpdatedAt => data?.UpdatedAt;
        public IReadOnlyList<(string Id, string Label)> Regions =>
            data?.Regions.Where(r => r.Id.StartsWith("global", StringComparison.Ordinal)).Select(r => (r.Id, r.Label)).ToList() ?? [];

        /// <summary>Downloads a fresh schedule if the copy is old; never throws.</summary>
        public async Task RefreshIfStaleAsync()
        {
            if (refreshing || DateTime.Now - lastRefresh < RefreshEvery) return;
            refreshing = true;
            try
            {
                string json = await Http.GetStringAsync(SourceUrl);
                data = Parse(json);
                await File.WriteAllTextAsync(cachePath, json);
                lastRefresh = DateTime.Now;
            }
            catch (Exception ex)
            {
                lastRefresh = DateTime.Now - RefreshEvery + TimeSpan.FromMinutes(10); // retry in 10 min
                logger.LogWarning("Event timers refresh failed: {Error}", ex.Message);
            }
            finally { refreshing = false; }
        }

        /// <summary>Every event's current/next occurrence for a region, live ones first, then soonest.</summary>
        public IReadOnlyList<EventStatus> GetStatus(string regionId, DateTime nowUtc)
        {
            if (data is null) return [];
            var region = data.Regions.FirstOrDefault(r => r.Id == regionId) ?? data.Regions.FirstOrDefault(r => r.Id == "global-naw");
            if (region is null) return [];
            var list = new List<EventStatus>();
            // Bosses the shugo.gg list doesn't have yet (skipped once it does), global regions only.
            var extra = region.Id.StartsWith("global", StringComparison.Ordinal)
                ? Supplement.Where(s => data.Events.All(e => !string.Equals(e.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
                : [];
            foreach (var ev in data.Events.Concat(extra))
            {
                if (!ev.Schedules.TryGetValue(region.Id, out var schedule) && !ev.Schedules.TryGetValue("global", out schedule)) continue;
                if (schedule.HideAfter is { } hide && nowUtc > hide) continue;
                var tz = FindZone(schedule.TimeZone ?? region.TimeZone);
                var duration = TimeSpan.FromMinutes(ev.DurationMinutes);
                DateTime? current = null, next = null;
                foreach (var start in Occurrences(schedule, tz, nowUtc))
                {
                    if (start <= nowUtc && nowUtc < start + duration) current = start;
                    else if (start > nowUtc && (next is null || start < next)) next = start;
                }
                if (current is null && next is null) continue;
                list.Add(new EventStatus(ev.Id, ev.Name, ev.Icon, ev.Kind, ev.Description, current, next, duration,
                    ev.PortalMinutes > 0 ? TimeSpan.FromMinutes(ev.PortalMinutes) : null));
            }
            return list.OrderBy(e => e.IsLive ? 0 : 1).ThenBy(e => e.IsLive ? e.LiveStart : e.Next).ToList();
        }

        /// <summary>Chaotic Middle Reshanta bosses (Lv 80), same clock as the other scheduled bosses (metabot.gg, from the game client).</summary>
        private static readonly IReadOnlyList<EventInfo> Supplement =
        [
            Boss("enraged-guardian-nahma", "Enraged Guardian Lord Nahma", "🐉", [DayOfWeek.Friday, DayOfWeek.Sunday], "21:00"),
            Boss("executioner-dramos", "Executioner Dramos", "⚔️", [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], "21:30"),
            Boss("ravager-marakha", "Ravager Marakha", "🔥", [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], "21:30"),
            Boss("turncoat-ducal", "Turncoat Ducal", "🗡️", [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], "21:30"),
        ];

        private static EventInfo Boss(string id, string name, string icon, DayOfWeek[] days, string time) =>
            new(id, name, icon, "boss", "Chaotic Middle Reshanta · Lv 80", 30, 0,
                new() { ["global"] = new Schedule("weekly", [TimeSpan.ParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture)], days.ToHashSet(), 0, null, null, null) });

        /// <summary>Start times (UTC) from yesterday through about a week ahead.</summary>
        private static IEnumerable<DateTime> Occurrences(Schedule s, TimeZoneInfo tz, DateTime nowUtc)
        {
            switch (s.Type)
            {
                case "hourly":
                    var hour = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
                    for (int h = -2; h <= 2; h++) yield return hour.AddHours(h).AddMinutes(s.Minute);
                    break;
                case "daily":
                case "weekly":
                    var localToday = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
                    for (int d = -1; d <= 8; d++)
                    {
                        var day = localToday.AddDays(d);
                        if (s.Type == "weekly" && !s.Days.Contains(day.DayOfWeek)) continue;
                        foreach (var t in s.Times)
                        {
                            var local = DateTime.SpecifyKind(day + t, DateTimeKind.Unspecified);
                            if (tz.IsInvalidTime(local)) continue; // skipped by a DST change
                            yield return TimeZoneInfo.ConvertTimeToUtc(local, tz);
                        }
                    }
                    break;
                case "once":
                    if (s.At is { } at) yield return at;
                    break;
            }
        }

        private static TimeZoneInfo FindZone(string? id)
        {
            if (string.IsNullOrEmpty(id) || id == "UTC") return TimeZoneInfo.Utc;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } // IANA ids work on Windows via ICU
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        }

        private static ScheduleData Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var regions = root.GetProperty("regions").EnumerateArray()
                .Select(r => new RegionInfo(Str(r, "id"), Str(r, "label"), Str(r, "timeZone"))).ToList();
            var events = new List<EventInfo>();
            foreach (var e in root.GetProperty("events").EnumerateArray())
            {
                var schedules = new Dictionary<string, Schedule>();
                foreach (var p in e.GetProperty("schedules").EnumerateObject())
                {
                    var v = p.Value;
                    schedules[p.Name] = new Schedule(
                        Str(v, "type"),
                        v.TryGetProperty("times", out var times) ? times.EnumerateArray().Select(t => TimeSpan.ParseExact(t.GetString()!, @"hh\:mm", CultureInfo.InvariantCulture)).ToList() : [],
                        v.TryGetProperty("days", out var days) ? days.EnumerateArray().Select(d => DayOf(d.GetString()!)).ToHashSet() : [],
                        v.TryGetProperty("minute", out var minute) ? minute.GetInt32() : 0,
                        v.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null,
                        v.TryGetProperty("at", out var at) ? at.GetDateTime().ToUniversalTime() : null,
                        v.TryGetProperty("hideAfter", out var hide) ? hide.GetDateTime().ToUniversalTime() : null);
                }
                events.Add(new EventInfo(Str(e, "id"), Str(e, "name"), Str(e, "icon"), Str(e, "kind"), Str(e, "description"),
                    e.TryGetProperty("durationMinutes", out var dur) ? dur.GetInt32() : 0,
                    e.TryGetProperty("portalMinutes", out var portal) && portal.ValueKind == JsonValueKind.Number ? portal.GetInt32() : 0,
                    schedules));
            }
            return new ScheduleData(Str(root, "updatedAt"), regions, events);
        }

        private static DayOfWeek DayOf(string d) => d.ToLowerInvariant() switch
        {
            "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday,
            "thu" => DayOfWeek.Thursday, "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
        };

        private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (AION2 DPS meter)");
            return client;
        }

        private sealed record ScheduleData(string UpdatedAt, List<RegionInfo> Regions, List<EventInfo> Events);
        private sealed record RegionInfo(string Id, string Label, string TimeZone);
        private sealed record EventInfo(string Id, string Name, string Icon, string Kind, string Description, int DurationMinutes, int PortalMinutes, Dictionary<string, Schedule> Schedules);
        private sealed record Schedule(string Type, List<TimeSpan> Times, HashSet<DayOfWeek> Days, int Minute, string? TimeZone, DateTime? At, DateTime? HideAfter);
    }

    public sealed record EventStatus(
        string Id, string Name, string Icon, string Kind, string Description,
        DateTime? LiveStart, DateTime? Next, TimeSpan Duration, TimeSpan? Portal)
    {
        public bool IsLive => LiveStart is not null;
        public bool PortalOpen(DateTime nowUtc) => LiveStart is { } s && Portal is { } p && nowUtc < s + p;
    }
}

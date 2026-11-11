using System.Text.Json.Serialization;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>
    /// Your skill casts, read from the game's cooldown packets (it only sends your own), so a fight can
    /// be scored on cooldown use: "Skill X: cast 8 of 10 possible times". A packet with a cooldown
    /// while the skill was ready is a cast; a smaller value mid-cooldown is a cooldown reduction.
    /// </summary>
    public sealed class CooldownTracker
    {
        /// <summary>Shorter cooldowns are basically spam skills / GCD and only add noise.</summary>
        public static readonly TimeSpan MinCooldown = TimeSpan.FromSeconds(4);
        /// <summary>Reaction time allowed per ready window before it counts as a missed cast.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Keep = TimeSpan.FromHours(2);
        private static readonly TimeSpan NewCastSlack = TimeSpan.FromMilliseconds(500);

        private readonly Lock gate = new();
        private readonly Dictionary<uint, SkillState> skills = new();

        private sealed class SkillState
        {
            public required string Name;
            public string? Icon;
            public TimeSpan Nominal;                 // longest cooldown seen = the skill's real cooldown
            public readonly List<Cast> Casts = new();
            public DateTime ReadyAt;                 // when the latest cast's cooldown ends (after reductions)
        }

        private sealed class Cast(DateTime at, DateTime readyAt)
        {
            public DateTime At { get; } = at;
            public DateTime ReadyAt { get; set; } = readyAt;
        }

        public void Record(uint skillId, string name, string? icon, TimeSpan left, DateTime at)
        {
            if (left <= TimeSpan.Zero) return;
            lock (gate)
            {
                if (!skills.TryGetValue(skillId, out var s))
                    skills[skillId] = s = new SkillState { Name = name, Icon = icon };
                if (left > s.Nominal) s.Nominal = left;

                bool wasReady = s.Casts.Count == 0 || at >= s.ReadyAt - NewCastSlack;
                var remaining = s.ReadyAt - at;
                if (wasReady || left > remaining + TimeSpan.FromSeconds(1)) // restarted with a longer cooldown = recast
                {
                    s.Casts.Add(new Cast(at, at + left));
                    s.ReadyAt = at + left;
                }
                else
                {
                    s.ReadyAt = at + left;                 // cooldown reduced
                    s.Casts[^1].ReadyAt = s.ReadyAt;
                }
                s.Casts.RemoveAll(c => at - c.At > Keep);
            }
        }

        /// <summary>Cooldown use in [from, to] for every skill (cooldown ≥ <see cref="MinCooldown"/>) you cast in that window.</summary>
        public IReadOnlyList<CooldownUsage> Analyze(DateTime from, DateTime to)
        {
            if (to <= from) return [];
            var result = new List<CooldownUsage>();
            lock (gate)
            {
                foreach (var (id, s) in skills)
                {
                    if (s.Nominal < MinCooldown) continue;
                    var inside = s.Casts.Where(c => c.At >= from && c.At <= to).ToList();
                    if (inside.Count == 0) continue;

                    // Ready at the start of the fight unless a cast before it was still cooling down.
                    var before = s.Casts.LastOrDefault(c => c.At < from);
                    var readyFrom = before is not null && before.ReadyAt > from ? before.ReadyAt : from;
                    double readySeconds = 0;
                    int missed = 0;

                    void Window(DateTime readyStart, DateTime usedAt)
                    {
                        var gap = usedAt - readyStart;
                        if (gap <= TimeSpan.Zero) return;
                        readySeconds += gap.TotalSeconds;
                        missed += (int)Math.Floor(Math.Max(0, (gap - Grace).TotalSeconds) / s.Nominal.TotalSeconds);
                    }

                    foreach (var c in inside)
                    {
                        Window(readyFrom, c.At);
                        readyFrom = c.ReadyAt;
                    }
                    if (readyFrom < to) Window(readyFrom, to);

                    result.Add(new CooldownUsage(id, s.Name, s.Icon, s.Nominal.TotalSeconds, inside.Count, inside.Count + missed, readySeconds));
                }
            }
            return result.OrderByDescending(u => u.Possible - u.Casts).ThenByDescending(u => u.CooldownSeconds).ToList();
        }

        /// <summary>
        /// Live nudge: skills with a cooldown of at least <paramref name="minCooldown"/>, cast within
        /// <paramref name="usedWithin"/> (so utility skills you never use stay quiet), that have been
        /// ready for longer than <paramref name="minIdle"/> counting from <paramref name="since"/>
        /// (the start of the fight). Longest cooldown first.
        /// </summary>
        public IReadOnlyList<ReadySkill> SittingReady(DateTime now, DateTime since, TimeSpan minCooldown, TimeSpan minIdle,
            TimeSpan usedWithin, IReadOnlySet<long> ignored)
        {
            var list = new List<ReadySkill>();
            lock (gate)
            {
                foreach (var (id, s) in skills)
                {
                    if (s.Nominal < minCooldown || ignored.Contains(id) || s.Casts.Count == 0) continue;
                    if (now - s.Casts[^1].At > usedWithin) continue;
                    var readySince = s.ReadyAt > since ? s.ReadyAt : since;
                    var idle = now - readySince;
                    if (idle > minIdle) list.Add(new ReadySkill(id, s.Name, s.Icon, s.Nominal, idle));
                }
            }
            return list.OrderByDescending(r => r.Cooldown).ToList();
        }

        /// <summary>
        /// Your skill by name (case-insensitive; its latest cooldown if several ids share the name): when it is ready
        /// again (cooldown reductions included) and when you last cast it. Null if you haven't cast it.
        /// </summary>
        public SkillCooldown? Find(string name)
        {
            lock (gate)
            {
                SkillCooldown? found = null;
                foreach (var s in skills.Values)
                {
                    if (s.Casts.Count == 0 || !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (found is null || s.Casts[^1].At > found.LastCast)
                        found = new SkillCooldown(s.Name, s.Icon, s.ReadyAt, s.Casts[^1].At);
                }
                return found;
            }
        }

        public sealed record SkillCooldown(string Name, string? Icon, DateTime ReadyAt, DateTime LastCast);

        public void Reset()
        {
            lock (gate) skills.Clear();
        }
    }

    public sealed record ReadySkill(long SkillId, string Name, string? Icon, TimeSpan Cooldown, TimeSpan ReadyFor);

    /// <param name="ReadySeconds">Time the skill sat off cooldown during the fight without being used.</param>
    public sealed record CooldownUsage(long SkillId, string Name, string? Icon, double CooldownSeconds, int Casts, int Possible, double ReadySeconds)
    {
        [JsonIgnore] public double Efficiency => Possible > 0 ? Casts * 100.0 / Possible : 100;
        [JsonIgnore] public int Missed => Possible - Casts;

        /// <summary>All skills together: casts / possible casts.</summary>
        public static double Overall(IEnumerable<CooldownUsage> usages)
        {
            var list = usages.ToList();
            int possible = list.Sum(u => u.Possible);
            return possible > 0 ? list.Sum(u => u.Casts) * 100.0 / possible : 100;
        }
    }
}

using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Session;

namespace AionDpsMeter.UI.Services.Records
{
    /// <summary>
    /// One player's fight, boiled down for comparison: damage numbers, rotation (how much of the fight
    /// you were actually hitting), skills and buff uptime. Built from a saved History fight.
    /// </summary>
    public sealed record FightAnalysis(
        Guid SessionId, string Boss, DateTime EndedAt, TimeSpan Duration, string PlayerName,
        long TotalDamage, double Dps, double CritRate, double BackRate, double PerfectRate,
        int Hits, double HitsPerMinute, double ActivePercent, TimeSpan LongestGap, TimeSpan FirstHitAfter,
        IReadOnlyList<SkillStats> Skills, IReadOnlyDictionary<string, double> BuffUptime,
        IReadOnlyList<CooldownUsage> Cooldowns)
    {
        /// <summary>
        /// A gap between two of your hits longer than this counts as idle time (moving, dodging, waiting):
        /// normal casting keeps hits well under it.
        /// </summary>
        public static readonly TimeSpan IdleGap = TimeSpan.FromSeconds(1.5);

        /// <summary>Your numbers in this fight, or (if you weren't in it) the top damage dealer's.</summary>
        public static FightAnalysis? From(HistorySessionSnapshot s, long? playerId = null)
        {
            var player = playerId is { } id
                ? s.PlayerStats.FirstOrDefault(p => p.PlayerId == id)
                : s.PlayerStats.FirstOrDefault(p => p.IsUser) ?? s.PlayerStats.OrderByDescending(p => p.TotalDamage).FirstOrDefault();
            if (player is null) return null;

            var duration = s.Duration;
            double seconds = Math.Max(1, duration.TotalSeconds);
            var hits = (s.HitsByPlayer.TryGetValue(player.PlayerId, out var h) ? h : [])
                .Where(x => !x.IsDot).Select(x => x.DateTime).OrderBy(t => t).ToList();
            var (idle, longest) = Gaps(hits);
            var firstHit = hits.Count > 0 ? hits[0] - s.SessionStart : TimeSpan.Zero;
            if (firstHit < TimeSpan.Zero) firstHit = TimeSpan.Zero;

            var buffs = (s.BuffStatsByPlayer.TryGetValue(player.PlayerId, out var b) ? b : [])
                .GroupBy(x => x.BuffName)
                .ToDictionary(g => g.Key, g => Math.Min(100, g.Sum(x => x.EffectiveDurationSeconds) / seconds * 100));

            return new FightAnalysis(
                s.SessionId, s.TargetName, s.SessionEnd, duration, player.PlayerName,
                player.TotalDamage, player.TotalDamage / seconds,
                player.CriticalRate, player.BackAttackRate, player.PerfectRate,
                player.HitCount, player.HitCount / (seconds / 60.0),
                Math.Clamp((1 - (idle + firstHit).TotalSeconds / seconds) * 100, 0, 100), longest, firstHit,
                (s.SkillStatsByPlayer.TryGetValue(player.PlayerId, out var sk) ? sk : []).OrderByDescending(x => x.TotalDamage).ToList(),
                buffs,
                player.IsUser ? s.Cooldowns : []); // the game only sends your own cooldowns
        }

        /// <summary>Idle time (each gap beyond <see cref="IdleGap"/>) and the longest gap between hits.</summary>
        internal static (TimeSpan Idle, TimeSpan Longest) Gaps(IReadOnlyList<DateTime> sortedHits)
        {
            var idle = TimeSpan.Zero;
            var longest = TimeSpan.Zero;
            for (int i = 1; i < sortedHits.Count; i++)
            {
                var gap = sortedHits[i] - sortedHits[i - 1];
                if (gap > longest) longest = gap;
                if (gap > IdleGap) idle += gap - IdleGap;
            }
            return (idle, longest);
        }
    }
}

using AionDpsMeter.Services.Services.Session.Persistence;

namespace AionDpsMeter.UI.Services.Records
{
    /// <summary>
    /// Your progress on one boss, from the fights saved in History. Fights are grouped into levels by the boss's
    /// max HP (a Nightmare boss gains HP each time it levels up, so a higher level is a different fight), and
    /// only one character's fights are compared (your main and your alts are kept apart). DPS is your damage
    /// over the whole fight for every entry, so fights saved before that change compare fairly.
    /// </summary>
    public sealed class BossProgressService(ICombatHistoryStore history)
    {
        /// <summary>Max HP values within this share of each other are the same level (HP reads can wobble slightly).</summary>
        private const double SameLevel = 0.02;
        /// <summary>Damage of at least this share of max HP counts as a kill (bosses can heal or have shields).</summary>
        private const double KillShare = 0.97;

        public BossProgress Get(string boss, string? character)
        {
            var fights = new List<BossFightEntry>();
            foreach (var f in history.GetBossFights(boss))
            {
                var me = f.Players.FirstOrDefault(p => p.IsUser && (character is null || string.Equals(p.PlayerName, character, StringComparison.OrdinalIgnoreCase)));
                if (me is null || me.TotalDamage <= 0) continue;
                var duration = f.End - f.Start;
                if (duration < TimeSpan.FromSeconds(5)) continue; // a stray hit, not a fight
                fights.Add(new BossFightEntry(f.SessionId, f.Start, duration, f.TargetHpTotal,
                    f.TargetHpTotal > 0 && f.TotalDamage >= f.TargetHpTotal * KillShare,
                    me.TotalDamage / Math.Max(1, duration.TotalSeconds), me.TotalDamage, me.DamagePercentage));
            }

            // Levels: distinct max HP, lowest first; unknown HP (0) is its own group at the end.
            var levels = new List<BossLevel>();
            foreach (var f in fights.Where(f => f.MaxHp > 0).OrderBy(f => f.MaxHp))
            {
                var level = levels.LastOrDefault();
                if (level is null || f.MaxHp > level.MaxHp * (1 + SameLevel))
                    levels.Add(level = new BossLevel(levels.Count + 1, f.MaxHp, new List<BossFightEntry>()));
                level.Fights.Add(f);
            }
            var unknown = fights.Where(f => f.MaxHp <= 0).ToList();
            if (unknown.Count > 0) levels.Add(new BossLevel(0, 0, unknown));
            foreach (var l in levels) l.Fights.Sort((a, b) => b.Start.CompareTo(a.Start)); // newest first
            return new BossProgress(boss, character, levels);
        }

        /// <summary>
        /// How a fight that just ended compares with your earlier fights on the same boss level, as one line
        /// (e.g. "Level 3 · +4% vs your best here (8,120/s) · 6th fight at this level"), and whether it is a new best.
        /// </summary>
        public (string? Line, bool NewBest) Compare(string boss, string? character, Guid sessionId, double dps, long maxHp)
        {
            var progress = Get(boss, character);
            var level = progress.Levels.FirstOrDefault(l => l.Fights.Any(f => f.SessionId == sessionId))
                        ?? progress.Levels.FirstOrDefault(l => l.Number > 0 && Math.Abs(l.MaxHp - maxHp) <= l.MaxHp * SameLevel);
            if (level is null) return (null, false);
            var earlier = level.Fights.Where(f => f.SessionId != sessionId).ToList();
            string where = level.Number > 0 ? $"Level {level.Number} ({Short(level.MaxHp)} HP)" : "This boss";
            if (earlier.Count == 0) return ($"{where} · first fight at this level", false);
            double best = earlier.Max(f => f.Dps);
            double diff = (dps / best - 1) * 100;
            bool newBest = dps > best;
            string vs = newBest ? $"new best, +{diff:F0}% over {best:N0}/s" : $"{(diff >= 0 ? "+" : "")}{diff:F0}% vs your best here ({best:N0}/s)";
            return ($"{where} · {vs} · fight #{earlier.Count + 1} at this level", newBest);
        }

        /// <summary>
        /// Kill speed on the same boss level for the fight that just ended (saved to History already): your last
        /// earlier kill's time, your fastest earlier kill, and which kill this is. Null when the level isn't known.
        /// </summary>
        public KillPace? Pace(string boss, string? character, Guid sessionId, long maxHp)
        {
            var progress = Get(boss, character);
            var level = progress.Levels.FirstOrDefault(l => l.Fights.Any(f => f.SessionId == sessionId))
                        ?? progress.Levels.FirstOrDefault(l => l.Number > 0 && Math.Abs(l.MaxHp - maxHp) <= l.MaxHp * SameLevel);
            if (level is null) return null;
            var earlierKills = level.Fights.Where(f => f.Killed && f.SessionId != sessionId).ToList(); // newest first
            return new KillPace(earlierKills.FirstOrDefault()?.Duration, earlierKills.Count > 0 ? earlierKills.Min(f => f.Duration) : null,
                earlierKills.Count + 1, level.Number);
        }

        /// <summary>
        /// Why a fight's DPS differs from your 2 fights before it on this boss (any level): DPS split into how often
        /// you hit (hits per second, time spent not hitting) and how hard (damage per hit, crits, back attacks),
        /// then the skills whose damage changed most and the boss's HP. Lines come ranked; Tone +1 better, -1 worse.
        /// </summary>
        public IReadOnlyList<WhyLine> Explain(string boss, string? character, Guid sessionId)
        {
            var all = Get(boss, character).Levels.SelectMany(l => l.Fights).OrderByDescending(f => f.Start).ToList();
            int at = all.FindIndex(f => f.SessionId == sessionId);
            if (at < 0) return [];
            var fights = all.Skip(at).Take(3).Select(f => Analyze(f.SessionId, character)).ToList();
            if (fights[0] is not { } now) return [];
            var before = fights.Skip(1).OfType<FightAnalysis>().ToList();
            if (before.Count == 0) return [new WhyLine("First saved fight on this boss: nothing to compare with yet.", 0)];

            double Avg(Func<FightAnalysis, double> f) => before.Average(f);
            static string Pct(double now, double was) => was > 0 ? ((now / was - 1) * 100).ToString("+0;-0;0") + "%" : "";
            static int Tone(double now, double was, double margin = 0.03) => was <= 0 ? 0 : now > was * (1 + margin) ? 1 : now < was * (1 - margin) ? -1 : 0;
            static double PerSecond(FightAnalysis f) => f.Hits / Math.Max(1, f.Duration.TotalSeconds);
            string vs = before.Count == 1 ? "the fight before" : $"your {before.Count} fights before";

            double hpsNow = PerSecond(now), hpsWas = Avg(PerSecond);
            double perHitNow = now.Dps / Math.Max(0.01, hpsNow), perHitWas = Avg(f => f.Dps / Math.Max(0.01, PerSecond(f)));
            double idleNow = now.Duration.TotalSeconds * (1 - now.ActivePercent / 100), idleWas = Avg(f => f.Duration.TotalSeconds * (1 - f.ActivePercent / 100));
            double dpsWas = Avg(f => f.Dps);

            var lines = new List<(double Weight, WhyLine Line)>
            {
                (double.MaxValue, new WhyLine($"DPS {now.Dps:N0}/s vs {dpsWas:N0}/s on {vs} ({Pct(now.Dps, dpsWas)})", Tone(now.Dps, dpsWas, 0.01))),
                (Math.Abs(Math.Log(hpsNow / Math.Max(0.01, hpsWas))), new WhyLine(
                    $"Hitting {(hpsNow >= hpsWas ? "more" : "less")} often: {hpsNow:F2} hits/s vs {hpsWas:F2} ({Pct(hpsNow, hpsWas)}) · not hitting {idleNow:F1}s vs {idleWas:F1}s",
                    Tone(hpsNow, hpsWas))),
                (Math.Abs(Math.Log(perHitNow / Math.Max(1, perHitWas))), new WhyLine(
                    $"Hits {(perHitNow >= perHitWas ? "bigger" : "smaller")}: {perHitNow:N0} per hit vs {perHitWas:N0} ({Pct(perHitNow, perHitWas)}) · crit {now.CritRate:F0}% vs {Avg(f => f.CritRate):F0}% · back {now.BackRate:F0}% vs {Avg(f => f.BackRate):F0}%",
                    Tone(perHitNow, perHitWas))),
            };

            // Skills whose damage per second changed most (more or fewer hits, or harder hits).
            var skillNames = now.Skills.Select(k => k.SkillName).Concat(before.SelectMany(f => f.Skills.Select(k => k.SkillName))).Distinct().ToList();
            foreach (var name in skillNames)
            {
                double Sps(FightAnalysis f) => (f.Skills.FirstOrDefault(k => k.SkillName == name)?.TotalDamage ?? 0) / Math.Max(1, f.Duration.TotalSeconds);
                double Casts(FightAnalysis f) => f.Skills.FirstOrDefault(k => k.SkillName == name)?.HitCount ?? 0;
                double diff = Sps(now) - Avg(Sps);
                if (Math.Abs(diff) < Math.Max(150, dpsWas * 0.015)) continue;
                lines.Add((Math.Abs(diff) / Math.Max(1, dpsWas), new WhyLine(
                    $"{name}: {(diff >= 0 ? "+" : "-")}{Math.Abs(diff):N0}/s · {Casts(now):0} hits vs {Avg(Casts):0.#}", diff > 0 ? 1 : -1)));
            }

            var ranked = lines.OrderByDescending(l => l.Weight).Select(l => l.Line).Take(6).ToList();
            long hpNow = all[at].MaxHp;
            double hpWas = all.Skip(at + 1).Take(before.Count).Average(f => (double)f.MaxHp);
            if (hpNow > 0 && hpWas > 0 && Math.Abs(hpNow / hpWas - 1) > 0.02)
                ranked.Add(new WhyLine($"Boss had {Pct(hpNow, hpWas)} max HP ({Short(hpNow)}): {(hpNow > hpWas ? "a higher level, the fight lasts longer" : "a lower level")}", 0));
            return ranked;
        }

        private FightAnalysis? Analyze(Guid sessionId, string? character)
        {
            var snapshot = history.GetSession(sessionId);
            if (snapshot is null) return null;
            var me = snapshot.PlayerStats.FirstOrDefault(p => p.IsUser && (character is null || string.Equals(p.PlayerName, character, StringComparison.OrdinalIgnoreCase)));
            return me is null ? null : FightAnalysis.From(snapshot, me.PlayerId);
        }

        public static string Short(long hp) =>
            hp >= 1_000_000_000 ? $"{hp / 1e9:0.##}B" : hp >= 1_000_000 ? $"{hp / 1e6:0.##}M" : hp >= 1_000 ? $"{hp / 1e3:0.#}K" : hp.ToString();
    }

    /// <param name="Tone">+1 helped your DPS, -1 hurt it, 0 neutral / information.</param>
    public sealed record WhyLine(string Text, int Tone);

    /// <summary>Kill speed vs earlier kills at the same boss level. Last/Best are null on the first kill there.</summary>
    public sealed record KillPace(TimeSpan? Last, TimeSpan? Best, int KillNumber, int Level);

    public sealed record BossProgress(string Boss, string? Character, IReadOnlyList<BossLevel> Levels);

    /// <param name="Number">1 = lowest max HP; 0 = fights saved without a max HP.</param>
    public sealed record BossLevel(int Number, long MaxHp, List<BossFightEntry> Fights)
    {
        public double BestDps => Fights.Count > 0 ? Fights.Max(f => f.Dps) : 0;
    }

    public sealed record BossFightEntry(Guid SessionId, DateTime Start, TimeSpan Duration, long MaxHp, bool Killed, double Dps, long Total, double Share);
}

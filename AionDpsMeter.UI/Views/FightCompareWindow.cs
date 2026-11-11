using System.Windows;
using System.Windows.Controls;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.UI.Services.Records;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Two fights side by side (usually this fight vs your best on the same boss): DPS and hit rates,
    /// rotation (active time, longest idle gap, opener), the skills that made the difference and buff
    /// uptime. ▲ / ▼ mark where this fight is better / worse than the reference.
    /// </summary>
    public sealed class FightCompareWindow : CardWindow
    {
        private static readonly double[] Cols = [86, 86, 62];

        public FightCompareWindow(FightAnalysis now, FightAnalysis reference, string referenceLabel, Window? near, double overlayScale)
            : base("AION2 Fight Compare", near, overlayScale, activatable: true)
        {
            var root = new StackPanel();
            root.Children.Add(Header("FIGHT COMPARE", "#8fb4ff", now.Boss,
                $"{now.PlayerName} · this fight {now.EndedAt:dd.MM HH:mm} vs {referenceLabel} {reference.EndedAt:dd.MM HH:mm}"));

            root.Children.Add(HeadRow("", "This fight", referenceLabel));
            root.Children.Add(Metric("DPS", now.Dps, reference.Dps, Num));
            root.Children.Add(Metric("Total damage", now.TotalDamage, reference.TotalDamage, Num));
            root.Children.Add(Metric("Fight time", now.Duration.TotalSeconds, reference.Duration.TotalSeconds, s => Clock(TimeSpan.FromSeconds(s)), lowerIsBetter: true));
            root.Children.Add(Metric("Crit", now.CritRate, reference.CritRate, Pct));
            root.Children.Add(Metric("Back attack", now.BackRate, reference.BackRate, Pct));
            root.Children.Add(Metric("Perfect", now.PerfectRate, reference.PerfectRate, Pct));

            root.Children.Add(SectionLabel("ROTATION"));
            root.Children.Add(Metric("Active time", now.ActivePercent, reference.ActivePercent, Pct, tip:
                $"Share of the fight you were hitting; gaps over {FightAnalysis.IdleGap.TotalSeconds:F1} s between hits count as idle."));
            root.Children.Add(Metric("Hits / min", now.HitsPerMinute, reference.HitsPerMinute, v => v.ToString("F0")));
            root.Children.Add(Metric("Longest gap", now.LongestGap.TotalSeconds, reference.LongestGap.TotalSeconds, v => $"{v:F1}s", lowerIsBetter: true));
            root.Children.Add(Metric("First hit after", now.FirstHitAfter.TotalSeconds, reference.FirstHitAfter.TotalSeconds, v => $"{v:F1}s", lowerIsBetter: true,
                tip: "Time from the start of the fight to your first hit (slow opener)."));

            bool cooldowns = now.Cooldowns.Count > 0 && reference.Cooldowns.Count > 0;
            if (cooldowns)
                root.Children.Add(Metric("Cooldown use", CooldownUsage.Overall(now.Cooldowns), CooldownUsage.Overall(reference.Cooldowns), Pct,
                    tip: "Casts of skills with a cooldown of 4 s or more, out of how many times you could have cast them."));

            // Skills: biggest DPS differences first, so the reason for a gap is at the top.
            root.Children.Add(SectionLabel("SKILLS (DPS, BIGGEST DIFFERENCE FIRST)"));
            double secNow = Math.Max(1, now.Duration.TotalSeconds), secRef = Math.Max(1, reference.Duration.TotalSeconds);
            var names = now.Skills.Select(s => s.SkillName).Union(reference.Skills.Select(s => s.SkillName));
            var skillRows = names.Select(n =>
                {
                    var a = now.Skills.Where(s => s.SkillName == n).ToList();
                    var b = reference.Skills.Where(s => s.SkillName == n).ToList();
                    return (Name: n, A: a.Sum(s => s.TotalDamage) / secNow, B: b.Sum(s => s.TotalDamage) / secRef,
                            HitsA: a.Sum(s => s.HitCount), HitsB: b.Sum(s => s.HitCount));
                })
                .OrderByDescending(r => Math.Abs(r.A - r.B)).Take(10);
            foreach (var r in skillRows)
                root.Children.Add(Metric(r.Name, r.A, r.B, v => Num(v),
                    tip: $"{r.Name}: {r.HitsA} hits this fight vs {r.HitsB} ({referenceLabel})"));

            if (cooldowns)
            {
                root.Children.Add(SectionLabel("COOLDOWNS (CASTS / POSSIBLE)"));
                foreach (var name in now.Cooldowns.Select(c => c.Name).Union(reference.Cooldowns.Select(c => c.Name)).Take(10))
                {
                    var a = now.Cooldowns.FirstOrDefault(c => c.Name == name);
                    var b = reference.Cooldowns.FirstOrDefault(c => c.Name == name);
                    var row = Metric(name, a?.Efficiency ?? 0, b?.Efficiency ?? 0, _ => "", tip:
                        $"{name} (cooldown {(a ?? b)!.CooldownSeconds:F0} s): this fight {Casts(a)}, {referenceLabel} {Casts(b)}");
                    // Show "8/10" instead of the percentage in the value columns.
                    ((TextBlock)((Grid)row).Children[1]).Text = Casts(a);
                    ((TextBlock)((Grid)row).Children[2]).Text = Casts(b);
                    root.Children.Add(row);
                }
            }

            var buffNames = now.BuffUptime.Keys.Union(reference.BuffUptime.Keys)
                .OrderByDescending(n => Math.Max(now.BuffUptime.GetValueOrDefault(n), reference.BuffUptime.GetValueOrDefault(n)))
                .Take(8).ToList();
            if (buffNames.Count > 0)
            {
                root.Children.Add(SectionLabel("BUFF UPTIME"));
                foreach (var n in buffNames)
                    root.Children.Add(Metric(n, now.BuffUptime.GetValueOrDefault(n), reference.BuffUptime.GetValueOrDefault(n), Pct));
            }

            SetCard(new ScrollViewer
            {
                Content = root, MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            }, 440);
        }

        private static FrameworkElement HeadRow(string first, string a, string b) =>
            TableRow(Cols, [Text(first, 10, "#6c7282"), Text(a, 10, "#9aa0b0", FontWeights.Bold), Text(b, 10, "#9aa0b0", FontWeights.Bold), Text("Δ", 10, "#9aa0b0", FontWeights.Bold)]);

        private static FrameworkElement Metric(string name, double a, double b, Func<double, string> format, bool lowerIsBetter = false, string? tip = null)
        {
            double diff = b != 0 ? (a / b - 1) * 100 : 0;
            bool better = lowerIsBetter ? a < b : a > b;
            string delta = Math.Abs(a - b) < 1e-9 || b == 0 ? "–" : $"{(better ? "▲" : "▼")}{Math.Abs(diff):F0}%";
            string color = delta == "–" ? "#6c7282" : better ? "#4adfc4" : "#ff8a7a";
            var row = TableRow(Cols, [
                Text(name, 11.5, "#e8e8ee"),
                Text(format(a), 11.5, "#ffffff", FontWeights.Bold),
                Text(format(b), 11.5, "#9aa0b0"),
                Text(delta, 11, color, FontWeights.Bold),
            ]);
            if (tip is not null) row.ToolTip = tip;
            return row;
        }

        private static string Pct(double v) => $"{v:F1}%";
        private static string Casts(AionDpsMeter.Services.Services.Session.CooldownUsage? u) => u is null ? "–" : $"{u.Casts}/{u.Possible}";
    }
}

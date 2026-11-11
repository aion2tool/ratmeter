using System.Windows;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Utils;
using AionDpsMeter.UI.Views;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Records
{
    /// <summary>
    /// Keeps the summary of the last boss fight (20 s or more). The card opens on request (the clipboard
    /// button on the meter) or, if enabled, automatically once you have been out of combat for a few
    /// seconds. An open card closes itself as soon as combat starts again, so it never sits on a fight.
    /// </summary>
    public sealed class FightSummaryService
    {
        private static readonly TimeSpan MinFight = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan QuietBeforeAutoShow = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan CloseOnCombatAfter = TimeSpan.FromSeconds(3);
        private readonly CombatSessionManager sessions;
        private FightSummary? pending;
        private FightSummaryWindow? window;
        private DateTime windowOpenedAt;
        private System.Windows.Threading.DispatcherTimer? timer;

        /// <summary>Last finished boss fight (null until the first one).</summary>
        public FightSummary? Last { get; private set; }

        /// <summary>This session's boss fight results, oldest first (for ◀ ▶ in the summary card).</summary>
        private readonly List<FightSummary> recent = new();
        private const int RecentKept = 50;
        public bool IsOpen => window is { IsVisible: true };
        private readonly IAppSettingsService settings;
        private readonly PersonalBestService bests;
        private readonly EntityTracker entities;
        private readonly ILogger<FightSummaryService> logger;
        private readonly AionDpsMeter.Services.Services.Network.NetworkStats? network;
        private readonly CooldownTracker cooldowns;

        // Created after PersonalBestService (it's a constructor dependency), so the personal best
        // is already updated for this fight when our handler runs.
        private readonly BossProgressService progress;
        private readonly DiscordPoster discord;

        public FightSummaryService(CombatSessionManager sessions, IAppSettingsService settings, PersonalBestService bests,
            EntityTracker entities, ILogger<FightSummaryService> logger, AionDpsMeter.Services.Services.Network.NetworkStats network,
            CooldownTracker cooldowns, BossProgressService progress, DiscordPoster discord)
        {
            this.discord = discord;
            this.progress = progress;
            this.cooldowns = cooldowns;
            this.network = network;
            this.sessions = sessions;
            this.settings = settings;
            this.bests = bests;
            this.entities = entities;
            this.logger = logger;
            sessions.BossSessionCompleted += OnBossFinished;
        }

        /// <summary>Clipboard button: open the last fight's card, or close it if it is open.</summary>
        public void ToggleLast()
        {
            if (IsOpen) { window!.Close(); return; }
            if (recent.Count > 0) ShowAt(recent.Count - 1);
            else if (Last is not null) Show(Last);
        }

        private FightResultToast? toast;

        /// <summary>Bottom-right result card on the game screen; clicking it opens this fight's summary.</summary>
        private void ShowToast(FightSummary summary)
        {
            toast?.Close();
            var meter = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            var screen = ScreenUtil.GameWorkArea(meter is { IsVisible: true } && meter.WindowState != WindowState.Minimized ? meter : null);
            var card = new FightResultToast(summary, screen, settings.OverlayScale, () => Show(summary));
            card.Closed += (_, _) => { if (ReferenceEquals(toast, card)) toast = null; };
            toast = card;
            card.Show();
        }

        /// <summary>Opens result <paramref name="index"/> of this session with ◀ ▶ to the others.</summary>
        private void ShowAt(int index)
        {
            index = Math.Clamp(index, 0, recent.Count - 1);
            var browse = new FightSummaryWindow.Browse($"{index + 1} / {recent.Count}",
                index > 0 ? () => ShowAt(index - 1) : null,
                index < recent.Count - 1 ? () => ShowAt(index + 1) : null);
            Show(recent[index], browse);
        }

        private void Show(FightSummary summary, FightSummaryWindow.Browse? browse = null)
        {
            if (browse is null && recent.IndexOf(summary) is >= 0 and var i) { ShowAt(i); return; }
            window?.Close();
            var meter = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            var card = new FightSummaryWindow(summary, meter, settings.OverlayScale, browse);
            card.Closed += (_, _) => { if (ReferenceEquals(window, card)) window = null; };
            window = card;
            windowOpenedAt = DateTime.Now;
            card.Show();
        }

        private void EnsureTimer()
        {
            if (timer is not null) return;
            timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                var now = DateTime.Now;
                // Auto-show only after a quiet moment, never in the middle of the next pull.
                if (pending is not null && now - sessions.LastDamageAt >= QuietBeforeAutoShow)
                {
                    Show(pending);
                    pending = null;
                }
                // Combat started again: get out of the way.
                if (IsOpen && sessions.LastDamageAt > windowOpenedAt + CloseOnCombatAfter)
                    window!.Close();
            };
            timer.Start();
        }

        private void OnBossFinished(TargetCombatSession session)
        {
            try
            {
                var duration = session.GetCombatDuration();
                if (duration < MinFight) return;
                var stats = session.GetPlayerStats().Where(p => p.IsIdentified || p.DamagePercentage > 1).ToList();
                if (stats.Count == 0) return;

                string boss = session.TargetInfo.Name;
                var mob = entities.GetTargetMob(session.TargetInfo.Id) ?? session.TargetInfo;
                bool killed = mob.IsDead;
                bool hide = settings.IsNicknameHidden;
                var rows = stats.OrderByDescending(p => p.TotalDamage).Select(p => new FightSummaryRow(
                    hide ? NicknameObfuscator.Mask(p.PlayerName) : p.PlayerName, p.ClassId, p.IsUser,
                    p.TotalDamage, p.DamagePerSecond, p.DamagePercentage, p.CriticalRate, p.PlayerDeaths, p.CombatPower, p.GearScore)).ToList();

                string? topSkill = null;
                double topShare = 0;
                string? bestNote = null;
                bool newBest = false;
                var me = stats.FirstOrDefault(p => p.IsUser);
                if (me is not null)
                {
                    var skills = session.GetSkillStats(me.PlayerId).OrderByDescending(s => s.TotalDamage).ToList();
                    if (skills.Count > 0 && me.TotalDamage > 0)
                    {
                        topSkill = skills[0].SkillName;
                        topShare = skills[0].TotalDamage * 100.0 / me.TotalDamage;
                    }
                    if (bests.LastNewRecord is { } rec && rec.Boss == boss && DateTime.Now - rec.At < TimeSpan.FromSeconds(10))
                    {
                        newBest = true;
                        bestNote = rec.PreviousBest > 0 ? $"🏆 New personal best! +{(rec.Dps / rec.PreviousBest - 1) * 100:F1}%" : "🏆 New personal best!";
                    }
                    else if (bests.Get(boss) is { BestDps: > 0 } record)
                    {
                        double diff = (me.DamagePerSecond / record.BestDps - 1) * 100;
                        bestNote = $"Personal best {record.BestDps:N0}/s · this fight {(diff >= 0 ? "+" : "")}{diff:F0}% · avg {record.AverageDps:N0}/s";
                    }
                }

                // Server stalls during the fight: the "lag" wasn't your connection's fault if these show up.
                string? lagNote = null;
                if (settings.LagStallAlert && network?.GetStalls(session.SessionStart, session.LastHitTime) is { Count: > 0 } stalls)
                    lagNote = $"⚠ Server lag: {stalls.Count} stall{(stalls.Count == 1 ? "" : "s")} during this fight (longest {stalls.Max(s => s.Length.TotalSeconds):F1} s, the server sent nothing)";

                // Your cooldown use: the overall share plus the skills you left on cooldown most.
                string? cooldownNote = null;
                if (me is not null && cooldowns.Analyze(session.SessionStart, session.LastHitTime) is { Count: > 0 } usage)
                {
                    var missed = usage.Where(u => u.Missed > 0).Take(3).Select(u => $"{u.Name} ×{u.Missed}").ToList();
                    cooldownNote = $"Cooldowns used {CooldownUsage.Overall(usage):F0}%" + (missed.Count > 0 ? $" · missed {string.Join(", ", missed)}" : " · none missed");
                }

                var summary = new FightSummary(boss, killed, duration, DateTime.Now, rows, topSkill, topShare, bestNote, newBest, session.SessionId, lagNote, cooldownNote,
                    me?.PlayerName, mob.HpTotal, session.IsPartialView(), Party: stats.Select(p => p.PlayerName).ToList());
                // Compare with your earlier fights on the same boss level (History; off the packet thread, the
                // fight is saved to History first): replaces the name-only personal-best note.
                if (me is not null)
                {
                    double myDps = me.DamagePerSecond;
                    var dispatcher = Application.Current?.Dispatcher;
                    Task.Run(() =>
                    {
                        var compared = summary;
                        try
                        {
                            var (line, best) = progress.Compare(boss, me.PlayerName, session.SessionId, myDps, mob.HpTotal);
                            if (line is not null) compared = summary with { BestNote = (best ? "🏆 " : "") + line, NewBest = best };
                            if (killed) compared = compared with { Pace = progress.Pace(boss, me.PlayerName, session.SessionId, mob.HpTotal) };
                        }
                        catch (Exception ex) { logger.LogWarning(ex, "Boss progress compare failed"); }
                        dispatcher?.BeginInvoke(() => Publish(compared));
                    });
                    return;
                }
                Application.Current?.Dispatcher.BeginInvoke(() => Publish(summary));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fight summary failed");
            }
        }

        /// <summary>UI thread: the summary is ready (result pop-up, clipboard button, auto-show).</summary>
        private void Publish(FightSummary summary)
        {
            // Killed boss (dungeon etc., not an alliance field boss, not the dummy): result image on the clipboard.
            if (settings.AutoCopyFightImage && summary.Killed && !summary.Alliance)
            {
                try
                {
                    Clipboard.SetImage(FightSummaryWindow.RenderImage(summary, settings.OverlayScale));
                    summary = summary with { Copied = true };
                    logger.LogInformation("Fight result image copied to the clipboard: {Boss}", summary.Boss);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Copying the fight result image failed"); }
            }
            try { discord.OnFight(summary); }
            catch (Exception ex) { logger.LogWarning(ex, "Discord post failed"); }
            recent.Add(summary);
            if (recent.Count > RecentKept) recent.RemoveAt(0);
            Last = summary;
            if (settings.FightResultPopup) ShowToast(summary);
            if (settings.AutoShowFightSummary) pending = summary;
            EnsureTimer();
        }
    }
}

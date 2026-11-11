using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Update;
using AionDpsMeter.Core.Windowing;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.Utils;
using Microsoft.AspNetCore.Components.Web;
using System.Collections.Concurrent;

namespace AionDpsMeter.UI.ViewModels
{
    public class MainDpsViewModel : IDisposable
    {
        private readonly CombatSessionManager sessionManager;
        private readonly IAppSettingsService settingsService;
        private readonly UpdateCheckerService updateChecker;
        private readonly IWindowManagerService windowManager;
        private readonly WindowHelper windowHelper;

        public readonly Dictionary<long, PlayerRenderState> PlayerStates = new();

        public List<PlayerRenderState> Players = new();

        public string CombatDuration = "00:00";
        public string TotalRaidDamageFormatted = "0/s";
        public string TotalDamageFormatted = "0";
        public string PingDisplay = "-- ms";
        public string PingColor = "#888888";
        public int PingLevel = 0;

        public bool HasActiveTarget;
        public string ActiveTargetName = string.Empty;
        /// <summary>Plain name of the monster being fought (no " · 3 mobs"), for the pet line.</summary>
        public string ActiveMobName = string.Empty;
        /// <summary>"🐾 pet  Lv2 54/75 · 21 left" while the target gives souls for an unfinished pet, else "".</summary>
        public string PetLine = string.Empty;
        private readonly AionDpsMeter.UI.Services.Pets.PetTracker? petTracker =
            App.AppHost?.Services.GetService(typeof(AionDpsMeter.UI.Services.Pets.PetTracker)) as AionDpsMeter.UI.Services.Pets.PetTracker;

        /// <summary>
        /// What the meter shows when there's nothing to show, so it always says why: the game isn't running, the game
        /// connection isn't found yet, or there's simply no fight ("Waiting for boss…" in Boss mode).
        /// </summary>
        public string IdleText = "Waiting for combat…";
        private DateTime idleCheckedAt = DateTime.MinValue;
        private AionDpsMeter.Services.PacketCapture.IPacketCaptureDevice? capture;

        private string ComputeIdleText()
        {
            capture ??= App.AppHost.Services.GetService(typeof(AionDpsMeter.Services.PacketCapture.IPacketCaptureDevice)) as AionDpsMeter.Services.PacketCapture.IPacketCaptureDevice;
            if (!AionDpsMeter.UI.Services.GameLink.GameLink.IsGameRunning()) return "Waiting for AION 2…";
            if (capture is { IsLocked: false }) return "Finding the game connection…";
            return settingsService.BossFocus ? "Waiting for boss…" : "Waiting for combat…";
        }
        public string ActiveTargetHpDisplay = string.Empty;
        public double ActiveTargetHpPercentage;
        /// <summary>Raw HP of the shown target (for the side meter's ring and time-to-kill).</summary>
        public long ActiveTargetHp;
        public long ActiveTargetHpMax;

        /// <summary>
        /// The game doesn't always send a mob's current HP (it showed "0 / 43,500 · 0%"); then estimate it
        /// as max HP minus the damage dealt to it this fight.
        /// </summary>
        /// <summary>
        /// Exact HP with thousands separators ("1,622,539 / 2,160,000"); from 10M max HP on, short ("77.47M / 160.00M"):
        /// the long form of a 160M field boss ran into the DPS text beside it (48.4% + 22.36K/s read as "48,422.36K/s").
        /// </summary>
        private static string HpText(long hp, long max) =>
            max >= 10_000_000 ? $"{DamageFormatter.Format(hp)} / {DamageFormatter.Format(max)}" : $"{hp:N0} / {max:N0}";

        // Boss HP when this target was first seen, for the alliance's real DPS (HP lost per second).
        private int hpRateTarget = -1;
        private long hpRateStartHp;
        private DateTime hpRateStartAt;

        private long HpOf(AionDpsMeter.Core.Models.Mob mob) =>
            mob.HpCurrent > 0 || mob.HpTotal <= 0
                ? mob.HpCurrent
                : Math.Max(0, mob.HpTotal - sessionManager.GetTargetDamage(mob.Id));

        public bool UpdateAvailable;
        public string UpdateVersionText = string.Empty;

        public double RowScale = 1.0;

        // Window Opacity setting, applied to panel backgrounds only so text stays sharp.
        public double PanelAlpha = 0.92;

        private readonly ConcurrentDictionary<string, string> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        private PeriodicTimer? _refreshTimer;
        private CancellationTokenSource _cts = new();

        // Redraw only when what's on screen changed; poll slower while idle.
        private static readonly TimeSpan ActiveRefresh = TimeSpan.FromMilliseconds(33);
        private static readonly TimeSpan IdleRefresh = TimeSpan.FromMilliseconds(250);
        private const int IdleTicksBeforeSlowdown = 60; // ~2s of no change at 30 fps
        private int _lastRenderHash;
        private int _unchangedTicks;

        private readonly Func<Task> onStateChanged;

        public MainDpsViewModel(CombatSessionManager sessionManager, IAppSettingsService settingsService, UpdateCheckerService updateChecker, IWindowManagerService windowManager, WindowHelper windowHelper, Func<Task> onStateChanged)
        {
            this.sessionManager = sessionManager;
            this.settingsService = settingsService;
            this.updateChecker = updateChecker;
            this.windowManager = windowManager;
            this.windowHelper = windowHelper;
            this.onStateChanged = onStateChanged;
        }

        public void Initialize()
        {
            sessionManager.PingUpdated += OnPingUpdated;
            settingsService.SettingsChanged += OnSettingsChanged;

            RowScale = settingsService.PlayerRowScale > 0 ? settingsService.PlayerRowScale : 1.0;
            PanelAlpha = settingsService.WindowOpacity;

            _ = CheckForUpdatesAsync();

            _refreshTimer = new PeriodicTimer(ActiveRefresh);
            _ = UpdateLoopAsync();
        }
        private async Task UpdateLoopAsync()
        {
            try
            {
                while (await _refreshTimer!.WaitForNextTickAsync(_cts.Token))
                {
                    UpdateData();
                }
            }
            catch (OperationCanceledException) { }
        }

        private readonly Dictionary<long, PlayerRenderState> healStates = new();
        private int _lastHealHash;

        /// <summary>Healing view (✚ on the meter): rows show healing done instead of damage.</summary>
        private void UpdateHealingData()
        {
            var stats = sessionManager.GetHealStats();
            CombatDuration = sessionManager.GetHealDuration().ToString(@"mm\:ss");
            long total = stats.Sum(s => s.Total);
            double hps = stats.Sum(s => s.PerSecond);
            HasActiveTarget = false;
            ActiveTargetName = stats.Count == 0 ? "Healing · waiting for heals" : $"Healing · {stats.Count} healer{(stats.Count == 1 ? "" : "s")}";
            ActiveTargetHpDisplay = string.Empty;
            TotalDamageFormatted = DamageFormatter.Format(total);
            TotalRaidDamageFormatted = $"{DamageFormatter.Format(hps)}/s";
            double top = stats.Count > 0 ? stats.Max(s => s.Total) : 0;
            bool relative = settingsService.RelativeProgressBar;
            var ids = new HashSet<long>();
            foreach (var s in stats)
            {
                ids.Add(s.PlayerId);
                if (!healStates.TryGetValue(s.PlayerId, out var p)) healStates[s.PlayerId] = p = new PlayerRenderState { PlayerId = s.PlayerId };
                p.IsUser = s.IsUser;
                p.ClassId = s.ClassId.ToString();
                p.ClassName = s.ClassName;
                p.ServerName = s.ServerName;
                p.TotalDamage = s.Total;
                p.TotalDamageFormatted = DamageFormatter.Format(s.Total);
                p.Dps = s.PerSecond;
                p.DpsFormatted = DamageFormatter.Format(s.PerSecond);
                p.DamagePercentage = s.Percentage;
                p.PlayerName = s.Name;
                p.PlayerNameDisplay = settingsService.IsNicknameHidden ? NicknameObfuscator.Mask(s.Name) : s.Name;
                p.DeathsDisplay = string.Empty;
                p.EffectivePercentage = relative && top > 0 ? s.Total * 100.0 / top : s.Percentage;
            }
            foreach (var gone in healStates.Keys.Where(k => !ids.Contains(k)).ToList()) healStates.Remove(gone);

            var hash = new HashCode();
            hash.Add(CombatDuration); hash.Add(ActiveTargetName); hash.Add(TotalDamageFormatted);
            foreach (var p in healStates.Values) { hash.Add(p.PlayerId); hash.Add(p.TotalDamageFormatted); hash.Add(Math.Round(p.EffectivePercentage, 1)); }
            int h = hash.ToHashCode();
            if (h == _lastHealHash) return;
            _lastHealHash = h;
            Players = healStates.Values.OrderByDescending(p => p.TotalDamage).ToList();
            onStateChanged.Invoke();
        }

        private void UpdateData()
        {
            if (settingsService.ShowHealing) { UpdateHealingData(); return; }
            _lastHealHash = 0;
            bool uiNeedsUpdate = false;

            // Every 2 s (checking for the game window isn't free).
            if (DateTime.Now - idleCheckedAt > TimeSpan.FromSeconds(2))
            {
                idleCheckedAt = DateTime.Now;
                var idle = ComputeIdleText();
                if (idle != IdleText) { IdleText = idle; uiNeedsUpdate = true; }
            }

            var combatDuration = sessionManager.GetCombatDuration();
            var newDuration = combatDuration.ToString(@"mm\:ss");
            if (CombatDuration != newDuration)
            {
                CombatDuration = newDuration;
                uiNeedsUpdate = true;
            }

            int aoeMobCount = settingsService.AllTargetsMode ? sessionManager.AllTargetsMobCount : 0;
            var targetInfo = aoeMobCount > 0 ? null : sessionManager.GetActiveTargetInfo();
            ActiveMobName = string.Empty;
            if (aoeMobCount > 0)
            {
                // All-targets mode: damage covers every mob in the pull, but still show an HP bar for
                // the boss in the pull, or else for the mob you're hitting right now.
                string mobs = aoeMobCount == 1 ? "1 mob" : $"{aoeMobCount} mobs";
                var boss = sessionManager.GetActiveBossFight();
                var current = sessionManager.GetActiveTargetInfo();
                (string Name, long Hp, long Max)? focus =
                    boss is { HpTotal: > 0 } ? (boss.Name, boss.HpCurrent, boss.HpTotal)
                    : current is { HpTotal: > 0 } ? (current.Name, HpOf(current), current.HpTotal)
                    : null;
                if (focus is { } f)
                {
                    HasActiveTarget = true;
                    ActiveTargetName = $"{f.Name} · {mobs}";
                    ActiveMobName = f.Name;
                    ActiveTargetHpPercentage = Math.Clamp((double)f.Hp / f.Max * 100, 0, 100);
                    ActiveTargetHpDisplay = HpText(f.Hp, f.Max);
                    ActiveTargetHp = f.Hp;
                    ActiveTargetHpMax = f.Max;
                }
                else
                {
                    HasActiveTarget = false;
                    ActiveTargetName = $"All targets · {mobs}";
                    ActiveTargetHpDisplay = string.Empty;
                    ActiveTargetHpPercentage = 0;
                }
                uiNeedsUpdate = true;
            }
            else if (targetInfo is not null)
            {
                HasActiveTarget = true;
                ActiveTargetName = targetInfo.Name;
                ActiveMobName = targetInfo.Name;
                long hpNow = HpOf(targetInfo);
                ActiveTargetHp = hpNow;
                ActiveTargetHpMax = targetInfo.HpTotal;
                ActiveTargetHpPercentage = targetInfo.HpTotal > 0 ? Math.Clamp((double)hpNow / targetInfo.HpTotal * 100, 0, 100) : 0;
                ActiveTargetHpDisplay = targetInfo.HpTotal > 0 ? HpText(hpNow, targetInfo.HpTotal) : string.Empty;
                // New target, or HP went back up (new try): restart the HP-lost clock.
                if (targetInfo.Id != hpRateTarget || hpNow > hpRateStartHp)
                {
                    hpRateTarget = targetInfo.Id;
                    hpRateStartHp = hpNow;
                    hpRateStartAt = DateTime.Now;
                }
                uiNeedsUpdate = true;
            }
            else if (HasActiveTarget)
            {
                HasActiveTarget = false;
                uiNeedsUpdate = true;
            }
            if (targetInfo is null && aoeMobCount == 0 && settingsService.BossFocus)
                ActiveTargetName = IdleText;

            // Stats are rebuilt from every hit of the fight, so fetch them once per frame
            // (GetPartyDps() fetched them a second time). Same formula as GetPartyDps.
            var allStats = sessionManager.PlayerStats;
            double partyDps = allStats.Sum(r => r.TotalDamage) / Math.Max(1.0, combatDuration.TotalSeconds);
            // Alliance fight: the game sends only your own hits, so the seen total is a few % of the real one. The boss's
            // HP is exact, so HP lost per second since the meter first saw it is everyone's DPS together ("≈").
            bool alliance = allStats.Count(r => !r.IsUser && r.HitCount == 0) >= 3;
            double hpSeconds = (DateTime.Now - hpRateStartAt).TotalSeconds;
            if (alliance && targetInfo is { HpTotal: > 0 } && targetInfo.Id == hpRateTarget && hpSeconds >= 5 && hpRateStartHp > ActiveTargetHp)
                TotalRaidDamageFormatted = $"≈{DamageFormatter.Format((hpRateStartHp - ActiveTargetHp) / hpSeconds)}/s";
            else
                TotalRaidDamageFormatted = $"{DamageFormatter.Format(partyDps)}/s";

            var currentStats = allStats
                .Where(r => r.IsIdentified || r.DamagePercentage > 1)
                .ToList();

            long topDamage = currentStats.Count > 0 ? currentStats.Max(x => x.TotalDamage) : 0;
            TotalDamageFormatted = DamageFormatter.Format(currentStats.Sum(x => x.TotalDamage));
            var currentIds = new HashSet<long>();

            bool isNicknameHidden = settingsService.IsNicknameHidden;
            bool showPlayerDeaths = settingsService.ShowPlayerDeaths;
            bool useRelativeBar = settingsService.RelativeProgressBar;

            foreach (var stat in currentStats)
            {
                currentIds.Add(stat.PlayerId);

                if (!PlayerStates.TryGetValue(stat.PlayerId, out var player))
                {
                    player = new PlayerRenderState { PlayerId = stat.PlayerId };
                    PlayerStates[stat.PlayerId] = player;
                }

                player.IsUser = stat.IsUser;
                player.ClassId = stat.ClassId.ToString();
                player.TotalDamage = stat.TotalDamage;
                player.TotalDamageFormatted = DamageFormatter.Format(stat.TotalDamage);
                player.DpsFormatted = DamageFormatter.Format(stat.DamagePerSecond);
                player.Dps = stat.DamagePerSecond;
                player.DamagePercentage = stat.DamagePercentage;
                player.CombatPower = DamageFormatter.Format(stat.CombatPower);
                player.IconUrl = ResolveClassIconUrl(stat.ClassId);
                player.ClassName = stat.ClassName;
                player.ServerName = stat.ServerName;
                player.ServerId = stat.ServerId;
                player.ClassIcon = stat.ClassIcon;
                player.CriticalRate = stat.CriticalRate;

                string rawName = stat.PlayerName;
                player.PlayerName = rawName;

                player.PlayerNameDisplay = isNicknameHidden
                    ? NicknameObfuscator.Mask(rawName)
                    : rawName;

                player.DeathsDisplay = (showPlayerDeaths && stat.PlayerDeaths > 0)
                    ? $"💀 {stat.PlayerDeaths}"
                    : string.Empty;

                double targetAbs = stat.DamagePercentage;
                double diffAbs = targetAbs - player.VisualAbsolutePercentage;
                if (Math.Abs(diffAbs) < 0.05) player.VisualAbsolutePercentage = targetAbs;
                else player.VisualAbsolutePercentage += diffAbs * 0.25;

                double targetRel = topDamage > 0 ? ((double)stat.TotalDamage / topDamage) * 100.0 : 0;
                double diffRel = targetRel - player.VisualRelativePercentage;
                if (Math.Abs(diffRel) < 0.05) player.VisualRelativePercentage = targetRel;
                else player.VisualRelativePercentage += diffRel * 0.25;

                player.EffectivePercentage = useRelativeBar
                    ? player.VisualRelativePercentage
                    : player.VisualAbsolutePercentage;

                uiNeedsUpdate = true;
            }

            var keysToRemove = PlayerStates.Keys.Where(k => !currentIds.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                PlayerStates.Remove(key);
                uiNeedsUpdate = true;
            }

            // Compare what would be drawn, so ping changes still show and an
            // unchanged screen (e.g. standing in town after a fight) isn't redrawn.
            PetLine = petTracker?.LineFor(ActiveMobName) ?? string.Empty;
            int renderHash = ComputeRenderHash();
            uiNeedsUpdate = renderHash != _lastRenderHash;

            if (uiNeedsUpdate)
            {
                _lastRenderHash = renderHash;
                _unchangedTicks = 0;
                if (_refreshTimer is not null && _refreshTimer.Period != ActiveRefresh)
                    _refreshTimer.Period = ActiveRefresh;

                Players = PlayerStates.Values
                    .OrderByDescending(p => p.TotalDamage)
                    .ToList();

                onStateChanged.Invoke();
            }
            else if (++_unchangedTicks >= IdleTicksBeforeSlowdown &&
                     _refreshTimer is not null && _refreshTimer.Period != IdleRefresh)
            {
                _refreshTimer.Period = IdleRefresh;
            }
        }

        private int ComputeRenderHash()
        {
            var hash = new HashCode();
            hash.Add(CombatDuration);
            hash.Add(TotalRaidDamageFormatted);
            hash.Add(TotalDamageFormatted);
            hash.Add(PingDisplay);
            hash.Add(HasActiveTarget);
            hash.Add(ActiveTargetName);
            hash.Add(PetLine);
            hash.Add(ActiveTargetHpDisplay);
            hash.Add(Math.Round(ActiveTargetHpPercentage, 1));
            hash.Add(PlayerStates.Count);
            foreach (var p in PlayerStates.Values)
            {
                hash.Add(p.PlayerId);
                hash.Add(p.TotalDamageFormatted);
                hash.Add(p.DpsFormatted);
                hash.Add(p.PlayerNameDisplay);
                hash.Add(p.DeathsDisplay);
                hash.Add(p.CombatPower);
                hash.Add(Math.Round(p.EffectivePercentage, 1));
            }
            return hash.ToHashCode();
        }

        private async Task CheckForUpdatesAsync()
        {
            var release = await updateChecker.CheckForUpdateAsync();
            if (release is not null)
            {
                UpdateVersionText = $"New version available: {release.Name}";
                UpdateAvailable = true;
                await onStateChanged.Invoke();
            }
        }

        private void OnPingUpdated(object? sender, int pingMs)
        {
            if (pingMs <= 0) return; // 0 = the game packet couldn't be read as a ping; keep the last real one
            PingDisplay = $"{pingMs} ms";
            if (pingMs < 60) { PingColor = "#4EC9B0"; PingLevel = 3; }
            else if (pingMs < 100) { PingColor = "#DCDCAA"; PingLevel = 2; }
            else if (pingMs < 200) { PingColor = "#CE9178"; PingLevel = 1; }
            else { PingColor = "#F44747"; PingLevel = 1; }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            RowScale = settingsService.PlayerRowScale > 0 ? settingsService.PlayerRowScale : 1.0;
            PanelAlpha = settingsService.WindowOpacity;
            onStateChanged.Invoke();
        }

        private string ResolveClassIconUrl(int classId)
        {
            string key = classId.ToString();
            if (_iconCache.TryGetValue(key, out var cached)) return cached;

            string resolved = $"/images/classes/{classId}.png";
            _iconCache[key] = resolved;
            return resolved;
        }

        public string GetPlayerRowClass(PlayerRenderState player)
        {
            if (!settingsService.UseClassColors)
            {
                return player.IsUser ? "is-me" : "player-color ";
            }
            else
            {
                return $"dps-class-{player.ClassId}";
            }
        }

        public string GetIsUserClass(PlayerRenderState player)
        {
            if (!settingsService.UseClassColors) return string.Empty;
            return player.IsUser ? "is-me-border" : string.Empty;
        }

        //public string GetProgressClass(PlayerRenderState player) => $"dps-class-{player.ClassId}";
        public string GetCombatScoreDisplay(PlayerRenderState player) => (string.IsNullOrWhiteSpace(player.CombatPower) || player.CombatPower == "0") ? "" : player.CombatPower;
        public double ClampPercent(double value) => Math.Max(0, Math.Min(100, value));

        public string GetPanelAlphaStyle() =>
            $"--panel-alpha:{PanelAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture)};";

        public string GetRowScaleStyle() => RowScale != 1.0
            ? $"zoom:{RowScale.ToString(System.Globalization.CultureInfo.InvariantCulture)};"
            : string.Empty;

        public void BeginDrag(MouseEventArgs _) => windowManager.Drag(WindowKey.Main);

        public void OpenHistory() => windowHelper.OpenHistory();
        public void OpenWhatsNew() => windowHelper.OpenWhatsNewWindow();

        public void OpenSettings() => windowHelper.OpenSettings();
        public void Minimize() => windowManager.Minimize(WindowKey.Main);
        public void Close() => windowManager.CloseApplication();
        public void DismissUpdate() { UpdateAvailable = false; onStateChanged.Invoke(); }
        public void OpenPlayerDetails(PlayerRenderState player) => windowHelper.OpenPlayerDetails(player);

        /// <summary>Player's gear page on the official site (uses the real name even when names are masked).</summary>
        public void OpenGearProfile(PlayerRenderState player) =>
            _ = AionDpsMeter.UI.Services.Characters.GearProfile.OpenAsync(player.PlayerName, player.ServerId);

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _refreshTimer?.Dispose();
            sessionManager.PingUpdated -= OnPingUpdated;
            settingsService.SettingsChanged -= OnSettingsChanged;
        }

    }
}

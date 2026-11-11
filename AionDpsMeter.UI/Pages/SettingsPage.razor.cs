using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Core.Windowing;
using AionDpsMeter.UI.Services.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AionDpsMeter.UI.Pages
{
    partial class SettingsPage(IAppSettingsService settings, WindowHelper windowHelper, IWindowManagerService windowManager,
        AionDpsMeter.UI.Services.Records.DiscordPoster discord, AionDpsMeter.UI.Services.Recording.BossRecorder recorder) : IDisposable
    {
        private List<Skill> AllSkills  => FilteredSkills();

        private int _windowOpacityPercent;
        private double _playerRowScale;
        private int _overlayScalePercent;
        private bool _capturingHotkey;
        private bool _capturingNetHotkey;
        private bool _capturingTimersHotkey;
        private bool _capturingBossHotkey;
        private bool _capturingLastFightHotkey;
        private bool _capturingRecordHotkey;
        private bool _capturingClearHotkey;
        private bool _capturingLockHotkey;
        private static readonly (string Id, string Label)[] _timerRegions =
        [
            ("global-naw", "NA West"), ("global-nae", "NA East"), ("global-eu", "Europe"),
            ("global-sa", "S. America"), ("global-as", "Asia"),
        ];
        private string _activeGroup = "general";
        private string _version = "1.0.0";


        private sealed record SettingsGroup(string Id, string Label, string Blurb, string Icon);
        private sealed record UiStyleOption(int Value, string Label, string PreviewClass);
        private sealed record BarThemeOption(int Value, string Label, string A, string B, string C);
        private sealed record OrderOption(OverlayOrderMode Value, string Label);


        // Line icons (24×24, stroked) for the sidebar.
        private readonly List<SettingsGroup> _groups = new()
    {
        new("general", "General", "How fights are counted on the meter.", "<path d='M3 12h4l3-8 4 16 3-8h4'/>"),
        new("look", "Appearance", "Layout, colours and size of the meter.", "<path d='M12 3s6 6.5 6 11a6 6 0 0 1-12 0c0-4.5 6-11 6-11z'/>"),
        new("window", "Window", "Where the meter sits and when it hides. It can live on a second screen too.", "<rect x='3' y='4' width='18' height='12' rx='2'/><path d='M8 20h8M12 16v4'/>"),
        new("hotkeys", "Hotkeys", "Click a box, then press the key combination.", "<rect x='2.5' y='6' width='19' height='12' rx='2'/><path d='M6.5 10h.01M10 10h.01M14 10h.01M17.5 10h.01M7.5 14h9'/>"),
        new("alerts", "Notifications", "What pops up after fights and in combat.", "<path d='M6 16v-5a6 6 0 0 1 12 0v5l1.5 2h-15z'/><path d='M10 20.5a2 2 0 0 0 4 0'/>"),
        new("timers", "Timers & Bosses", "Field bosses, scheduled bosses and event reminders.", "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>"),
        new("recording", "Recording", "Automatic videos of boss fights.", "<rect x='3' y='6' width='13' height='12' rx='2'/><path d='M16 10.5l5-3v9l-5-3'/>"),
        new("data", "Data", "Fight history and troubleshooting.", "<ellipse cx='12' cy='6' rx='8' ry='3'/><path d='M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6'/><path d='M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3'/>"),
    };

        private SettingsGroup ActiveGroup => _groups.FirstOrDefault(g => g.Id == _activeGroup) ?? _groups[0];

        private readonly List<UiStyleOption> _uiStyles = new()
    {
        new(1, "Standard", "standard"),
        new(2, "Compact", "compact"),
        new(5, "Slim", "slim"),
    };


        // Swatches mirror ThemePalettes in NativeDpsOverlay.Themes.cs.
        private readonly List<BarThemeOption> _barThemes = new()
    {
        new(0, "Default", "#9c27b0", "#4caf50", "#26c6da"),
        new(1, "Ember", "#ff5e28", "#ff961e", "#ffc83c"),
        new(2, "Ocean", "#288cff", "#1ec8e6", "#50a0dc"),
        new(3, "Aurora", "#3ce696", "#28c8dc", "#9664ff"),
        new(4, "Rainbow", "#ff4650", "#f0e13c", "#3296ff"),
        new(5, "Royal Gold", "#f2c14e", "#cdd2dc", "#cd7f32"),
        new(6, "Sakura", "#ff78be", "#f096e6", "#ffaab4"),
        new(7, "Toxic", "#aaff28", "#28ff96", "#dcf03c"),
        new(8, "Mono", "#f2c14e", "#aaafbe", "#aaafbe"),
    };

        private readonly List<OrderOption> _orderOptions = new()
    {
        new(OverlayOrderMode.Ascending, "Least time"),
        new(OverlayOrderMode.Descending, "Most time"),
    };

        protected override void OnInitialized()
        {
            _windowOpacityPercent = (int)Math.Round(settings.WindowOpacity * 100);
            _playerRowScale = settings.PlayerRowScale;
            _overlayScalePercent = (int)Math.Round(settings.OverlayScale * 100);

            var asmVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            if (asmVersion is not null)
                _version = $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";

            recorder.StateChanged += OnRecorderChanged;
        }

        private void OnRecorderChanged() => InvokeAsync(StateHasChanged);

        private void ToggleRecording() => recorder.ToggleManual();



        private bool _autoStart = AionDpsMeter.UI.Services.GameLink.GameLink.IsAutoStartEnabled;

        private void SetAutoStart(bool enabled)
        {
            AionDpsMeter.UI.Services.GameLink.GameLink.SetAutoStart(enabled);
            _autoStart = AionDpsMeter.UI.Services.GameLink.GameLink.IsAutoStartEnabled;
        }

        /// <summary>0 off, 1 only when 2+ players are on the meter, 2 always.</summary>
        private void SetClassColors(int mode)
        {
            settings.UseClassColors = mode == 2;
            settings.ClassColorsInGroup = mode >= 1;
        }

        private string _query = string.Empty;
        private bool Searching => !string.IsNullOrWhiteSpace(_query);

        /// <summary>A tab's settings show when it is the open tab, or for every tab while searching.</summary>
        private bool IsShown(string group) => Searching || _activeGroup == group;

        private void SelectGroup(string id)
        {
            _activeGroup = id;
            _query = string.Empty;
        }

        private void CloseWindow() => windowHelper.CloseSettings();

        private void MinimizeWindow() => windowManager.Minimize(WindowKey.Settings);


        private List<Skill> FilteredSkills()
        {
           return  GameDataProvider.Instance.Skills.Skills
               .Where(r=>r.Key > 10000000).Select(r => r.Value).ToList();
        }

        public void Dispose()
        {
            recorder.StateChanged -= OnRecorderChanged;
        }

        private void OnWindowOpacityInput(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var pct))
            {
                _windowOpacityPercent = pct;
                settings.WindowOpacity = pct / 100.0;
            }
        }

        private void OnLockedOpacityInput(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var pct)) settings.LockedOpacity = pct / 100.0;
        }

        private void OnPreRollSecondsInput(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var secs)) settings.RecorderPreRollSeconds = secs;
        }

        private void OnOverlayScaleInput(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var pct))
            {
                _overlayScalePercent = pct;
                settings.OverlayScale = pct / 100.0;
            }
        }

        private void OnPlayerRowScaleInput(ChangeEventArgs e)
        {
            if (double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var scale))
            {
                _playerRowScale = scale;
                settings.PlayerRowScale = scale;
            }
        }

        private void SetUiStyle(int value) => settings.UiStyle = value;

        private void OpenRecordingFolder()
        {
            System.IO.Directory.CreateDirectory(settings.RecorderFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{settings.RecorderFolder}\"") { UseShellExecute = true });
        }

        /// <summary>0 = mob by mob, 1 = all mobs in the pull, 2 = boss only.</summary>
        private void SetMeterMode(int mode)
        {
            settings.AllTargetsMode = mode == 1;
            settings.BossFocus = mode == 2;
        }

        private void OnRetentionPeriodInput(ChangeEventArgs e)
        {
            var raw = e.Value?.ToString() ?? string.Empty;
            var digitsOnly = new string(raw.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length == 0) return;
            if (int.TryParse(digitsOnly, out var days))
                settings.HistoryRetantionPeriod = Math.Clamp(days, 1, 9999);
        }

        private void OnHotkeyFocus(FocusEventArgs e) => _capturingHotkey = true;

        private void OnHotkeyBlur(FocusEventArgs e) => _capturingHotkey = false;

        private static readonly HashSet<string> IgnoredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shift", "Control", "Alt", "Meta", "Tab", "Escape"
    };

        private void OnHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.ToggleVisibilityHotkey = combo;
            _capturingHotkey = false;
        }

        private void OnNetHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.NetworkOverlayHotkey = combo;
            _capturingNetHotkey = false;
        }

        // Event ids from shugo.gg timers.json.
        private static readonly (string Id, string Label)[] _notifyEvents =
        [
            ("rift", "Spacetime Rift"), ("artifact-siege", "Artifact Siege"), ("siege-bosses", "Siege Bosses"),
            ("guardian-nahma", "Nahma"), ("watcher-kaira", "Kaira"), ("shugo-festival", "Shugo Festival"),
            ("dimensional-invasion", "Dimensional Invasion"), ("daily-reset", "Daily reset"), ("weekly-reset", "Weekly reset"),
        ];

        private bool IsPinned(string id) =>
            settings.PinnedEventIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(id);

        private void TogglePinned(string id)
        {
            var ids = settings.PinnedEventIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!ids.Remove(id)) ids.Add(id);
            settings.PinnedEventIds = string.Join(",", ids);
        }

        private bool IsNotifyOn(string id) =>
            settings.EventNotifyIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(id);

        private void ToggleNotify(string id)
        {
            var ids = settings.EventNotifyIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!ids.Remove(id)) ids.Add(id);
            settings.EventNotifyIds = string.Join(",", ids);
        }

        private readonly AionDpsMeter.UI.Services.Timers.KillTimerService _killTimers =
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AionDpsMeter.UI.Services.Timers.KillTimerService>(App.AppHost.Services);

        /// <summary>"Respawns 7:42 PM (in 3h12)" / "UP since 7:42 PM" / "No kill recorded".</summary>
        private string KillStatus(AionDpsMeter.UI.Services.Timers.KillBoss boss)
        {
            if (_killTimers.LastKill(boss.Id) is not { } killed) return "No kill recorded";
            var spawn = killed + boss.Respawn;
            var left = spawn - DateTime.UtcNow;
            string at = spawn.ToLocalTime().ToString(spawn.ToLocalTime().Date == DateTime.Today ? "t" : "ddd t");
            return left <= TimeSpan.Zero
                ? $"UP since {at} (killed {killed.ToLocalTime():ddd t})"
                : $"Respawns {at} (in {(int)left.TotalHours}h{left.Minutes:00}) · killed {killed.ToLocalTime():ddd t}";
        }

        /// <summary>Time box: kill at that clock time today, or yesterday if that's still ahead.</summary>
        private void OnKillTimeChanged(string id, ChangeEventArgs e)
        {
            if (!TimeSpan.TryParse(e.Value?.ToString(), out var time)) return;
            var local = DateTime.Today + time;
            if (local > DateTime.Now) local = local.AddDays(-1);
            _killTimers.MarkKilled(id, local.ToUniversalTime());
        }

        private static readonly (string Id, string Label)[] _bossAlertEvents =
        [
            ("watcher-kaira", "Watcher Kaira"),
            ("guardian-nahma", "Guardian Lord Nahma"),
            ("siege-bosses", "Siege Bosses"),
        ];

        private bool IsBossAlertOn(string id) =>
            settings.BossAlertIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(id);

        private void ToggleBossAlert(string id)
        {
            var ids = settings.BossAlertIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!ids.Remove(id)) ids.Add(id);
            settings.BossAlertIds = string.Join(",", ids);
        }

        private void OnLockHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.LockHotkey = combo;
            _capturingLockHotkey = false;
        }

        private void OnClearHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.ClearMeterHotkey = combo;
            _capturingClearHotkey = false;
        }

        private void OnLastFightHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.LastFightHotkey = combo;
            _capturingLastFightHotkey = false;
        }

        private void OnRecordHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.RecordHotkey = combo;
            _capturingRecordHotkey = false;
        }

        private void OnBossHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.BossAlertsHotkey = combo;
            _capturingBossHotkey = false;
        }

        private void OnTimersHotkeyKeyDown(KeyboardEventArgs e)
        {
            if (FormatHotkey(e) is not { } combo) return;
            settings.EventTimersHotkey = combo;
            _capturingTimersHotkey = false;
        }

        private static string? FormatHotkey(KeyboardEventArgs e)
        {
            if (IgnoredKeys.Contains(e.Key)) return null;

            var parts = new List<string>();
            if (e.CtrlKey) parts.Add("Ctrl");
            if (e.ShiftKey) parts.Add("Shift");
            if (e.AltKey) parts.Add("Alt");

            var keyName = e.Key.Length == 1 ? e.Key.ToUpperInvariant() : e.Key;
            parts.Add(keyName);
            return string.Join("+", parts);
        }

        /// <summary>After a boss fight: 0 nothing, 1 result card, 2 card + summary image on the clipboard.</summary>
        private int AfterFight => !settings.FightResultPopup ? 0 : settings.AutoCopyFightImage ? 2 : 1;

        private string? _discordTest;

        private async Task SendDiscordTest()
        {
            _discordTest = "Sending…";
            _discordTest = await discord.SendTestAsync() is { } err ? "✗ " + err : "✓ Sent: check the channel";
        }

        private void SetAfterFight(int mode)
        {
            settings.FightResultPopup = mode >= 1;
            settings.AutoCopyFightImage = mode == 2;
            settings.AutoShowFightSummary = false; // the card opens the summary on click
        }

        /// <summary>Auto-hide: 0 never, 1 out of combat, 2 out of combat and in menus.</summary>
        private int AutoHide => !settings.AutoHideOutOfCombat && !settings.HideInMenus ? 0 : settings.HideInMenus ? 2 : 1;

        private void SetAutoHide(int mode)
        {
            settings.AutoHideOutOfCombat = mode >= 1;
            settings.HideInMenus = mode == 2;
        }

        // ---------------- Skill ready picker

        private static readonly (int Prefix, string Name)[] _classes =
        [
            (11, "Gladiator"), (12, "Templar"), (13, "Assassin"), (14, "Ranger"), (15, "Sorcerer"),
            (16, "Elementalist"), (17, "Cleric"), (18, "Chanter"), (19, "Brawler"),
        ];
        private bool _skillPickerOpen;
        private int _skillClass = 12;
        private string _skillQuery = string.Empty;

        private string[] ReadySkills =>
            settings.ReadyAlertSkills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>Player skills (class 11-19: Gladiator ... Brawler).</summary>
        private static IEnumerable<Skill> PlayerSkills =>
            GameDataProvider.Instance.Skills.Skills.Values.Where(s => s.ClassId is >= 11 and <= 19);

        /// <summary>The picked class's skills, one per name (charge levels, weapon swaps and basic attack left out).</summary>
        private IEnumerable<(string Name, string? Icon)> ClassSkills => PlayerSkills
            .Where(s => s.ClassId == _skillClass)
            .Where(s => !s.Name.Contains('_') && !s.Name.Contains('[') && !s.Name.Contains("Level ") && !s.Name.Contains("Basic Attack")
                        && !s.Name.EndsWith(" MAX", StringComparison.OrdinalIgnoreCase))
            .Where(s => _skillQuery.Length == 0 || s.Name.Contains(_skillQuery, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.Name)
            .Select(g => (g.Key, g.FirstOrDefault(s => s.HasIcon)?.Icon))
            .OrderBy(s => s.Key);

        private static string? IconFor(string name) =>
            PlayerSkills.FirstOrDefault(s => s.HasIcon && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Icon;

        private void OpenSkillPicker()
        {
            _skillPickerOpen = !_skillPickerOpen;
            if (!_skillPickerOpen) return;
            // Start on the class of the skills already picked.
            if (ReadySkills.FirstOrDefault() is { } first &&
                PlayerSkills.FirstOrDefault(s => s.Name.Equals(first, StringComparison.OrdinalIgnoreCase)) is { } skill)
                _skillClass = skill.ClassId;
        }

        private void ToggleReadySkill(string name)
        {
            var list = ReadySkills.ToList();
            if (list.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(name);
            settings.ReadyAlertSkills = string.Join(", ", list);
        }

        // ---------------- Field boss picks

        private readonly AionDpsMeter.UI.Services.Timers.FieldBossTimers _fieldBosses =
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AionDpsMeter.UI.Services.Timers.FieldBossTimers>(App.AppHost.Services);

        private IReadOnlyList<(string Name, string Level, bool Better)> FieldBossList => _fieldBosses.BossesForSide(_killTimers.AsmodianSide);

        private string[] FieldPicks =>
            settings.FieldBossPicks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>0 custom picks, 1 higher-level only, 2 all.</summary>
        private int FieldPreset => FieldPicks.Length > 0 ? 0 : settings.FieldBossAlertsHigherOnly ? 1 : 2;

        private void SetFieldPreset(int preset)
        {
            if (preset == 0)
            {
                if (FieldPicks.Length == 0) // start from what is on now
                    settings.FieldBossPicks = string.Join(",", FieldBossList.Where(IsFieldBossOn).Select(b => b.Name).DefaultIfEmpty("-"));
                return;
            }
            settings.FieldBossPicks = string.Empty;
            settings.FieldBossAlertsHigherOnly = preset == 1;
        }

        private bool IsFieldBossOn((string Name, string Level, bool Better) boss) => FieldPicks.Length > 0
            ? FieldPicks.Contains(boss.Name, StringComparer.OrdinalIgnoreCase)
            : !settings.FieldBossAlertsHigherOnly || boss.Better;

        private void ToggleFieldBoss(string name)
        {
            var list = FieldPicks.Length > 0
                ? FieldPicks.Where(n => n != "-").ToList()
                : FieldBossList.Where(IsFieldBossOn).Select(b => b.Name).ToList();
            if (list.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(name);
            settings.FieldBossPicks = list.Count == 0 ? "-" : string.Join(",", list); // "-": none picked
        }

        private void BeginDrag(MouseEventArgs _) => windowManager.Drag(WindowKey.Settings);
    }
}
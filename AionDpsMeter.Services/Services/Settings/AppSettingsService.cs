using AionDpsMeter.Core.Windowing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDpsMeter.Services.Services.Settings
{
    public sealed class AppSettingsService : IAppSettingsService
    {
        private const string SettingsFilePath = "appsettings.user.json";
        private readonly Lock _lock = new();
        private AppSettingsData _data;

        public event EventHandler? SettingsChanged;

        public AppSettingsService()
        {
            _data = Load();
            _data.HistoryRetantionPeriod = Math.Clamp(_data.HistoryRetantionPeriod, 1, 9999);
            _data.WindowBoundsByKey ??= [];
            MoveHotkeysOffShift();
        }

        /// <summary>
        /// The old defaults were Ctrl+Shift+…, but Shift is dodge in AION 2 and the keys are taken system-wide,
        /// so dodging could toggle the meter. Hotkeys still on an old default move to the new Ctrl+Alt ones;
        /// hotkeys the user picked are left alone.
        /// </summary>
        private void MoveHotkeysOffShift()
        {
            var fresh = new AppSettingsData();
            bool changed = false;
            string Move(string current, string oldDefault, string newDefault)
            {
                if (!string.Equals(current, oldDefault, StringComparison.OrdinalIgnoreCase)) return current;
                changed = true;
                return newDefault;
            }
            _data.ToggleVisibilityHotkey = Move(_data.ToggleVisibilityHotkey, "Ctrl+Shift+D", fresh.ToggleVisibilityHotkey);
            _data.NetworkOverlayHotkey = Move(_data.NetworkOverlayHotkey, "Ctrl+Shift+F1", fresh.NetworkOverlayHotkey);
            _data.EventTimersHotkey = Move(_data.EventTimersHotkey, "Ctrl+Shift+F2", fresh.EventTimersHotkey);
            _data.ClearMeterHotkey = Move(_data.ClearMeterHotkey, "Ctrl+Shift+F3", fresh.ClearMeterHotkey);
            _data.LockHotkey = Move(_data.LockHotkey, "Ctrl+Shift+F4", fresh.LockHotkey);
            if (changed) Save();
        }

        public OverlaySettings BufOverlaySettings
        {
            get { lock (_lock) return _data.BufOverlaySettings; }
            set
            {
                lock (_lock)
                {
                    _data.BufOverlaySettings = value;
                    Save();
                } SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public OverlaySettings SkillCdOverlaySettings
        {
            get { lock (_lock) return _data.SkillCdOverlaySettings; }
            set
            {
                lock (_lock)
                {
                    _data.SkillCdOverlaySettings = value;
                    Save();
                }
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }


        public bool UseClassColors
        {
            get { lock (_lock) return _data.UseClassColors; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.UseClassColors != value;
                    _data.UseClassColors = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TryGetWindowBounds(WindowKey key, out WindowBounds? bounds)
        {
            lock (_lock)
            {
                return _data.WindowBoundsByKey.TryGetValue(key.ToString(), out bounds);
            }
        }

        public void SetWindowBounds(WindowKey key, WindowBounds bounds)
        {
            lock (_lock)
            {
                _data.WindowBoundsByKey[key.ToString()] = bounds;
                Save();
            }
        }



        public double PlayerRowScale
        {
            get { lock (_lock) return _data.PlayerRowScale; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.PlayerRowScale != value;
                    _data.PlayerRowScale = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcCritChance
        {
            get { lock (_lock) return _data.StatCalcCritChance; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Clamp(value, 0, 1000);
                    changed = _data.StatCalcCritChance != clamped;
                    _data.StatCalcCritChance = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcBackAttackRate
        {
            get { lock (_lock) return _data.StatCalcBackAttackRate; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Clamp(value, 0, 1000);
                    changed = _data.StatCalcBackAttackRate != clamped;
                    _data.StatCalcBackAttackRate = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcFrontAttackRate
        {
            get { lock (_lock) return _data.StatCalcFrontAttackRate; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Clamp(value, 0, 1000);
                    changed = _data.StatCalcFrontAttackRate != clamped;
                    _data.StatCalcFrontAttackRate = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string StatCalcAttackType
        {
            get { lock (_lock) return _data.StatCalcAttackType; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    var normalized = value is "Front" or "Back" ? value : "None";
                    changed = _data.StatCalcAttackType != normalized;
                    _data.StatCalcAttackType = normalized;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcAttackIncreaseCombatPercent
        {
            get { lock (_lock) return _data.StatCalcAttackIncreaseCombatPercent; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Max(0, value);
                    changed = _data.StatCalcAttackIncreaseCombatPercent != clamped;
                    _data.StatCalcAttackIncreaseCombatPercent = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcPartyDamageBoost
        {
            get { lock (_lock) return _data.StatCalcPartyDamageBoost; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Max(0, value);
                    changed = _data.StatCalcPartyDamageBoost != clamped;
                    _data.StatCalcPartyDamageBoost = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcBossDamageTolerance
        {
            get { lock (_lock) return _data.StatCalcBossDamageTolerance; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Max(0, value);
                    changed = _data.StatCalcBossDamageTolerance != clamped;
                    _data.StatCalcBossDamageTolerance = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcPartySmiteBuff
        {
            get { lock (_lock) return _data.StatCalcPartySmiteBuff; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Max(0, value);
                    changed = _data.StatCalcPartySmiteBuff != clamped;
                    _data.StatCalcPartySmiteBuff = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double StatCalcBossSmiteResist
        {
            get { lock (_lock) return _data.StatCalcBossSmiteResist; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Max(0, value);
                    changed = _data.StatCalcBossSmiteResist != clamped;
                    _data.StatCalcBossSmiteResist = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowPlayerDeaths
        {
            get { lock (_lock) return _data.ShowPlayerDeaths; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowPlayerDeaths != value;
                    _data.ShowPlayerDeaths = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsPacketLoggingEnabled
        {
            get { lock (_lock) return _data.IsPacketLoggingEnabled; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.IsPacketLoggingEnabled != value;
                    _data.IsPacketLoggingEnabled = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsNicknameHidden
        {
            get { lock (_lock) return _data.IsNicknameHidden; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.IsNicknameHidden != value;
                    _data.IsNicknameHidden = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int UiStyle
        {
            get { lock (_lock) return _data.UiStyle; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.UiStyle != value;
                    _data.UiStyle = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }


        public bool AllTargetsMode
        {
            get { lock (_lock) return _data.AllTargetsMode; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.AllTargetsMode != value;
                    _data.AllTargetsMode = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double OverlayScale
        {
            get { lock (_lock) return _data.OverlayScale; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Clamp(value, 0.75, 3.0);
                    changed = _data.OverlayScale != clamped;
                    _data.OverlayScale = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool RecorderEnabled
        {
            get { lock (_lock) return _data.RecorderEnabled; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderEnabled != value;
                    _data.RecorderEnabled = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string RecorderFolder
        {
            get { lock (_lock) return _data.RecorderFolder; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderFolder != value;
                    _data.RecorderFolder = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int RecorderQuality
        {
            get { lock (_lock) return _data.RecorderQuality; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderQuality != value;
                    _data.RecorderQuality = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool RecorderPvp
        {
            get { lock (_lock) return _data.RecorderPvp; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderPvp != value;
                    _data.RecorderPvp = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool RecorderAudio
        {
            get { lock (_lock) return _data.RecorderAudio; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderAudio != value;
                    _data.RecorderAudio = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int RecorderMonitor
        {
            get { lock (_lock) return _data.RecorderMonitor; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderMonitor != value;
                    _data.RecorderMonitor = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string BossAlertsHotkey
        {
            get { lock (_lock) return _data.BossAlertsHotkey; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BossAlertsHotkey != value;
                    _data.BossAlertsHotkey = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string EventTimersHotkey
        {
            get { lock (_lock) return _data.EventTimersHotkey; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventTimersHotkey != value;
                    _data.EventTimersHotkey = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string TimerRegion
        {
            get { lock (_lock) return _data.TimerRegion; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.TimerRegion != value;
                    _data.TimerRegion = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool AutoTimerRegion
        {
            get { lock (_lock) return _data.AutoTimerRegion; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.AutoTimerRegion != value;
                    _data.AutoTimerRegion = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TimersRiftAlert
        {
            get { lock (_lock) return _data.TimersRiftAlert; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.TimersRiftAlert != value;
                    _data.TimersRiftAlert = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool EventNotifications
        {
            get { lock (_lock) return _data.EventNotifications; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventNotifications != value;
                    _data.EventNotifications = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool EventNotifySound
        {
            get { lock (_lock) return _data.EventNotifySound; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventNotifySound != value;
                    _data.EventNotifySound = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string EventNotifyIds
        {
            get { lock (_lock) return _data.EventNotifyIds; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventNotifyIds != value;
                    _data.EventNotifyIds = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool BossAlerts
        {
            get { lock (_lock) return _data.BossAlerts; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BossAlerts != value;
                    _data.BossAlerts = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string BossAlertIds
        {
            get { lock (_lock) return _data.BossAlertIds; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BossAlertIds != value;
                    _data.BossAlertIds = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int EventNotifyLeadMinutes
        {
            get { lock (_lock) return _data.EventNotifyLeadMinutes; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventNotifyLeadMinutes != value;
                    _data.EventNotifyLeadMinutes = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TimersShowAll
        {
            get { lock (_lock) return _data.TimersShowAll; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.TimersShowAll != value;
                    _data.TimersShowAll = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool AutoResetOnZone
        {
            get { lock (_lock) return _data.AutoResetOnZone; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.AutoResetOnZone != value;
                    _data.AutoResetOnZone = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string ClearMeterHotkey
        {
            get { lock (_lock) return _data.ClearMeterHotkey; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ClearMeterHotkey != value;
                    _data.ClearMeterHotkey = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string PinnedEventIds
        {
            get { lock (_lock) return _data.PinnedEventIds; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.PinnedEventIds != value;
                    _data.PinnedEventIds = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowGearScore
        {
            get { lock (_lock) return _data.ShowGearScore; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowGearScore != value;
                    _data.ShowGearScore = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool FightResultPopup
        {
            get { lock (_lock) return _data.FightResultPopup; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.FightResultPopup != value;
                    _data.FightResultPopup = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool PetTracker
        {
            get { lock (_lock) return _data.PetTracker; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.PetTracker != value;
                    _data.PetTracker = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool FieldBossMapAlerts
        {
            get { lock (_lock) return _data.FieldBossMapAlerts; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.FieldBossMapAlerts != value;
                    _data.FieldBossMapAlerts = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool FieldBossAlertsHigherOnly
        {
            get { lock (_lock) return _data.FieldBossAlertsHigherOnly; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.FieldBossAlertsHigherOnly != value;
                    _data.FieldBossAlertsHigherOnly = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool CloseWithGame
        {
            get { lock (_lock) return _data.CloseWithGame; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.CloseWithGame != value;
                    _data.CloseWithGame = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool MeterOnTop
        {
            get { lock (_lock) return _data.MeterOnTop; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.MeterOnTop != value;
                    _data.MeterOnTop = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool HideInMenus
        {
            get { lock (_lock) return _data.HideInMenus; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.HideInMenus != value;
                    _data.HideInMenus = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool AutoHideOutOfCombat
        {
            get { lock (_lock) return _data.AutoHideOutOfCombat; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.AutoHideOutOfCombat != value;
                    _data.AutoHideOutOfCombat = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int AutoHideSeconds
        {
            get { lock (_lock) return _data.AutoHideSeconds; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.AutoHideSeconds != value;
                    _data.AutoHideSeconds = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowHealing
        {
            get { lock (_lock) return _data.ShowHealing; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowHealing != value;
                    _data.ShowHealing = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int EventDockMode
        {
            get { lock (_lock) return _data.EventDockMode; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventDockMode != value;
                    _data.EventDockMode = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int EventDockSide
        {
            get { lock (_lock) return _data.EventDockSide; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EventDockSide != value;
                    _data.EventDockSide = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowEventsTab
        {
            get { lock (_lock) return _data.ShowEventsTab; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowEventsTab != value;
                    _data.ShowEventsTab = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool HighlightSelf
        {
            get { lock (_lock) return _data.HighlightSelf; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.HighlightSelf != value;
                    _data.HighlightSelf = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ClassColorsInGroup
        {
            get { lock (_lock) return _data.ClassColorsInGroup; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ClassColorsInGroup != value;
                    _data.ClassColorsInGroup = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool LockClickThrough
        {
            get { lock (_lock) return _data.LockClickThrough; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.LockClickThrough != value;
                    _data.LockClickThrough = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string LockHotkey
        {
            get { lock (_lock) return _data.LockHotkey; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.LockHotkey != value;
                    _data.LockHotkey = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool AutoShowFightSummary
        {
            get { lock (_lock) return _data.AutoShowFightSummary; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.AutoShowFightSummary != value;
                    _data.AutoShowFightSummary = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowFightSummary
        {
            get { lock (_lock) return _data.ShowFightSummary; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowFightSummary != value;
                    _data.ShowFightSummary = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int EdgePartyMode
        {
            get { lock (_lock) return _data.EdgePartyMode; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.EdgePartyMode != value;
                    _data.EdgePartyMode = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int RecorderMaxGB
        {
            get { lock (_lock) return _data.RecorderMaxGB; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RecorderMaxGB != value;
                    _data.RecorderMaxGB = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int MeterLayout
        {
            get { lock (_lock) return _data.MeterLayout; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.MeterLayout != value;
                    _data.MeterLayout = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool BossFocus
        {
            get { lock (_lock) return _data.BossFocus; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BossFocus != value;
                    _data.BossFocus = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool BarBurst
        {
            get { lock (_lock) return _data.BarBurst; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BarBurst != value;
                    _data.BarBurst = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool BarRankGlow
        {
            get { lock (_lock) return _data.BarRankGlow; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BarRankGlow != value;
                    _data.BarRankGlow = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool BarOnlyMine
        {
            get { lock (_lock) return _data.BarOnlyMine; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BarOnlyMine != value;
                    _data.BarOnlyMine = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int BarEffect
        {
            get { lock (_lock) return _data.BarEffect; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BarEffect != value;
                    _data.BarEffect = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int BarTheme
        {
            get { lock (_lock) return _data.BarTheme; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BarTheme != value;
                    _data.BarTheme = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowPanelBackground
        {
            get { lock (_lock) return _data.ShowPanelBackground; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowPanelBackground != value;
                    _data.ShowPanelBackground = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ShowTotalDamage
        {
            get { lock (_lock) return _data.ShowTotalDamage; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ShowTotalDamage != value;
                    _data.ShowTotalDamage = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int AllTargetsGapSeconds
        {
            get { lock (_lock) return _data.AllTargetsGapSeconds; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    int clamped = Math.Clamp(value, 3, 60);
                    changed = _data.AllTargetsGapSeconds != clamped;
                    _data.AllTargetsGapSeconds = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool BossOnlyCapture
        {
            get { lock (_lock) return _data.BossOnlyCapture; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BossOnlyCapture != value;
                    _data.BossOnlyCapture = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int HistoryRetantionPeriod
        {
            get { lock (_lock) return _data.HistoryRetantionPeriod; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    int clamped = Math.Clamp(value, 1, 9999);
                    changed = _data.HistoryRetantionPeriod != clamped;
                    _data.HistoryRetantionPeriod = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double WindowOpacity
        {
            get { lock (_lock) return _data.WindowOpacity; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    double clamped = Math.Clamp(value, 0.1, 1.0);
                    changed = _data.WindowOpacity != clamped;
                    _data.WindowOpacity = clamped;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string NetworkOverlayHotkey
        {
            get { lock (_lock) return _data.NetworkOverlayHotkey; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.NetworkOverlayHotkey != value;
                    _data.NetworkOverlayHotkey = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string ToggleVisibilityHotkey
        {
            get { lock (_lock) return _data.ToggleVisibilityHotkey; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.ToggleVisibilityHotkey != value;
                    _data.ToggleVisibilityHotkey = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public double? WindowLeft
        {
            get { lock (_lock) return _data.WindowLeft; }
            set { lock (_lock) { _data.WindowLeft = value; Save(); } }
        }

        public double? WindowTop
        {
            get { lock (_lock) return _data.WindowTop; }
            set { lock (_lock) { _data.WindowTop = value; Save(); } }
        }

        public double? WindowWidth
        {
            get { lock (_lock) return _data.WindowWidth; }
            set { lock (_lock) { _data.WindowWidth = value; Save(); } }
        }

        public double? WindowHeight
        {
            get { lock (_lock) return _data.WindowHeight; }
            set { lock (_lock) { _data.WindowHeight = value; Save(); } }
        }

        public string? BackgroundImagePath
        {
            get { lock (_lock) return _data.BackgroundImagePath; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.BackgroundImagePath != value;
                    _data.BackgroundImagePath = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool RelativeProgressBar
        {
            get { lock (_lock) return _data.RelativeProgressBar; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.RelativeProgressBar != value;
                    _data.RelativeProgressBar = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool GroupSummonDamage
        {
            get { lock (_lock) return _data.GroupSummonDamage; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.GroupSummonDamage != value;
                    _data.GroupSummonDamage = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool OneMinuteDummyMode
        {
            get { lock (_lock) return _data.OneMinuteDummyMode; }
            set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _data.OneMinuteDummyMode != value;
                    _data.OneMinuteDummyMode = value;
                    if (changed) Save();
                }
                if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // ----- newer settings: same save / change-event behaviour as above, via one helper

        private void Set<T>(Func<AppSettingsData, T> get, Action<AppSettingsData, T> set, T value)
        {
            bool changed;
            lock (_lock)
            {
                changed = !EqualityComparer<T>.Default.Equals(get(_data), value);
                if (changed) { set(_data, value); Save(); }
            }
            if (changed) SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public double LockedOpacity
        {
            get { lock (_lock) return _data.LockedOpacity; }
            set => Set(d => d.LockedOpacity, (d, v) => d.LockedOpacity = v, Math.Round(Math.Clamp(value, 0.1, 1.0), 2));
        }

        public bool LockedRightClickGear
        {
            get { lock (_lock) return _data.LockedRightClickGear; }
            set => Set(d => d.LockedRightClickGear, (d, v) => d.LockedRightClickGear = v, value);
        }

        public bool RecorderPreRoll
        {
            get { lock (_lock) return _data.RecorderPreRoll; }
            set => Set(d => d.RecorderPreRoll, (d, v) => d.RecorderPreRoll = v, value);
        }

        public int RecorderPreRollSeconds
        {
            get { lock (_lock) return _data.RecorderPreRollSeconds; }
            set => Set(d => d.RecorderPreRollSeconds, (d, v) => d.RecorderPreRollSeconds = v, Math.Clamp(value, 5, 60));
        }

        public int RecorderRetentionDays
        {
            get { lock (_lock) return _data.RecorderRetentionDays; }
            set => Set(d => d.RecorderRetentionDays, (d, v) => d.RecorderRetentionDays = v, Math.Max(0, value));
        }

        public bool LagStallAlert
        {
            get { lock (_lock) return _data.LagStallAlert; }
            set => Set(d => d.LagStallAlert, (d, v) => d.LagStallAlert = v, value);
        }

        public bool DeathRecap
        {
            get { lock (_lock) return _data.DeathRecap; }
            set => Set(d => d.DeathRecap, (d, v) => d.DeathRecap = v, value);
        }

        public bool ShowHotkeyTips
        {
            get { lock (_lock) return _data.ShowHotkeyTips; }
            set => Set(d => d.ShowHotkeyTips, (d, v) => d.ShowHotkeyTips = v, value);
        }

        public bool PartyOnly
        {
            get { lock (_lock) return _data.PartyOnly; }
            set => Set(d => d.PartyOnly, (d, v) => d.PartyOnly = v, value);
        }

        public bool OnlyMyFights
        {
            get { lock (_lock) return _data.OnlyMyFights; }
            set => Set(d => d.OnlyMyFights, (d, v) => d.OnlyMyFights = v, value);
        }

        public bool DummyLongRun
        {
            get { lock (_lock) return _data.DummyLongRun; }
            set => Set(d => d.DummyLongRun, (d, v) => d.DummyLongRun = v, value);
        }

        public bool AutoCopyFightImage
        {
            get { lock (_lock) return _data.AutoCopyFightImage; }
            set => Set(d => d.AutoCopyFightImage, (d, v) => d.AutoCopyFightImage = v, value);
        }

        public bool DiscordShareEnabled
        {
            get { lock (_lock) return _data.DiscordShareEnabled; }
            set => Set(d => d.DiscordShareEnabled, (d, v) => d.DiscordShareEnabled = v, value);
        }

        public string DiscordWebhookUrl
        {
            get { lock (_lock) return _data.DiscordWebhookUrl; }
            set => Set(d => d.DiscordWebhookUrl, (d, v) => d.DiscordWebhookUrl = v, (value ?? "").Trim());
        }

        public string DiscordFriends
        {
            get { lock (_lock) return _data.DiscordFriends; }
            set => Set(d => d.DiscordFriends, (d, v) => d.DiscordFriends = v, value ?? "");
        }

        public int DiscordMinFriends
        {
            get { lock (_lock) return _data.DiscordMinFriends; }
            set => Set(d => d.DiscordMinFriends, (d, v) => d.DiscordMinFriends = v, Math.Clamp(value, 1, 5));
        }

        public string RecordHotkey
        {
            get { lock (_lock) return _data.RecordHotkey; }
            set => Set(d => d.RecordHotkey, (d, v) => d.RecordHotkey = v, value ?? string.Empty);
        }

        public string LastFightHotkey
        {
            get { lock (_lock) return _data.LastFightHotkey; }
            set => Set(d => d.LastFightHotkey, (d, v) => d.LastFightHotkey = v, value ?? string.Empty);
        }

        public bool ReadyAlert
        {
            get { lock (_lock) return _data.ReadyAlert; }
            set => Set(d => d.ReadyAlert, (d, v) => d.ReadyAlert = v, value);
        }

        public string FieldBossPicks
        {
            get { lock (_lock) return _data.FieldBossPicks; }
            set => Set(d => d.FieldBossPicks, (d, v) => d.FieldBossPicks = v, value ?? string.Empty);
        }

        public int FieldBossLeadMinutes
        {
            get { lock (_lock) return _data.FieldBossLeadMinutes; }
            set => Set(d => d.FieldBossLeadMinutes, (d, v) => d.FieldBossLeadMinutes = v, Math.Clamp(value, 1, 60));
        }

        public string ReadyAlertSkills
        {
            get { lock (_lock) return _data.ReadyAlertSkills; }
            set => Set(d => d.ReadyAlertSkills, (d, v) => d.ReadyAlertSkills = v, value ?? string.Empty);
        }

        public bool ReadyAlertSound
        {
            get { lock (_lock) return _data.ReadyAlertSound; }
            set => Set(d => d.ReadyAlertSound, (d, v) => d.ReadyAlertSound = v, value);
        }

        public bool CooldownNudge
        {
            get { lock (_lock) return _data.CooldownNudge; }
            set => Set(d => d.CooldownNudge, (d, v) => d.CooldownNudge = v, value);
        }

        public int CooldownNudgeMinCooldown
        {
            get { lock (_lock) return _data.CooldownNudgeMinCooldown; }
            set => Set(d => d.CooldownNudgeMinCooldown, (d, v) => d.CooldownNudgeMinCooldown = v, Math.Clamp(value, 5, 120));
        }

        public int CooldownNudgeAfter
        {
            get { lock (_lock) return _data.CooldownNudgeAfter; }
            set => Set(d => d.CooldownNudgeAfter, (d, v) => d.CooldownNudgeAfter = v, Math.Clamp(value, 1, 15));
        }

        public IReadOnlyList<long> CooldownNudgeIgnored
        {
            get { lock (_lock) return _data.CooldownNudgeIgnored.ToList(); }
            set
            {
                lock (_lock) { _data.CooldownNudgeIgnored = value.Distinct().ToList(); Save(); }
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private AppSettingsData Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
                }
            }
            catch { }
            return new AppSettingsData();
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }

        private sealed class AppSettingsData
        {
            [JsonPropertyName("isPacketLoggingEnabled")]
            public bool IsPacketLoggingEnabled { get; set; }

            [JsonPropertyName("isNicknameHidden")]
            public bool IsNicknameHidden { get; set; }

            [JsonPropertyName("bossOnlyCapture")]
            public bool BossOnlyCapture { get; set; }

            [JsonPropertyName("allTargetsMode")]
            public bool AllTargetsMode { get; set; } = true;

            [JsonPropertyName("allTargetsGapSeconds")]
            public int AllTargetsGapSeconds { get; set; } = 10;

            [JsonPropertyName("lockedOpacity")]
            public double LockedOpacity { get; set; } = 0.35;

            [JsonPropertyName("lockedRightClickGear")]
            public bool LockedRightClickGear { get; set; }

            [JsonPropertyName("recorderPreRoll")]
            public bool RecorderPreRoll { get; set; }

            [JsonPropertyName("recorderPreRollSeconds")]
            public int RecorderPreRollSeconds { get; set; } = 15;

            [JsonPropertyName("recorderRetentionDays")]
            public int RecorderRetentionDays { get; set; } = 3;

            [JsonPropertyName("lagStallAlert")]
            public bool LagStallAlert { get; set; } = true;

            [JsonPropertyName("deathRecap")]
            public bool DeathRecap { get; set; } = true;

            [JsonPropertyName("showHotkeyTips")]
            public bool ShowHotkeyTips { get; set; } = true;

            [JsonPropertyName("cooldownNudge")]
            public bool CooldownNudge { get; set; } = true;

            [JsonPropertyName("partyOnly")]
            public bool PartyOnly { get; set; } = false;

            [JsonPropertyName("onlyMyFights")]
            public bool OnlyMyFights { get; set; } = true;

            [JsonPropertyName("dummyLongRun")]
            public bool DummyLongRun { get; set; } = true;

            [JsonPropertyName("autoCopyFightImage")]
            public bool AutoCopyFightImage { get; set; } = false;

            [JsonPropertyName("discordShareEnabled")]
            public bool DiscordShareEnabled { get; set; }

            [JsonPropertyName("discordWebhookUrl")]
            public string DiscordWebhookUrl { get; set; } = "";

            [JsonPropertyName("discordFriends")]
            public string DiscordFriends { get; set; } = "";

            [JsonPropertyName("discordMinFriends")]
            public int DiscordMinFriends { get; set; } = 1;

            [JsonPropertyName("recordHotkey")]
            public string RecordHotkey { get; set; } = "Ctrl+Alt+R";

            [JsonPropertyName("lastFightHotkey")]
            public string LastFightHotkey { get; set; } = "Ctrl+Alt+F";

            [JsonPropertyName("readyAlert")]
            public bool ReadyAlert { get; set; } = true;

            [JsonPropertyName("fieldBossPicks")]
            public string FieldBossPicks { get; set; } = string.Empty;

            [JsonPropertyName("fieldBossLeadMinutes")]
            public int FieldBossLeadMinutes { get; set; } = 5;

            [JsonPropertyName("readyAlertSkills")]
            public string ReadyAlertSkills { get; set; } = string.Empty;

            [JsonPropertyName("readyAlertSound")]
            public bool ReadyAlertSound { get; set; } = true;

            [JsonPropertyName("cooldownNudgeMinCooldown")]
            public int CooldownNudgeMinCooldown { get; set; } = 15;

            [JsonPropertyName("cooldownNudgeAfter")]
            public int CooldownNudgeAfter { get; set; } = 3;

            [JsonPropertyName("cooldownNudgeIgnored")]
            public List<long> CooldownNudgeIgnored { get; set; } = new();

            [JsonPropertyName("overlayScale")]
            public double OverlayScale { get; set; } = 1.0;

            [JsonPropertyName("recorderEnabled")]
            public bool RecorderEnabled { get; set; } = false;

            [JsonPropertyName("recorderFolder")]
            public string RecorderFolder { get; set; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "AION2 Recordings");

            [JsonPropertyName("recorderQuality")]
            public int RecorderQuality { get; set; } = 1;

            [JsonPropertyName("recorderPvp")]
            public bool RecorderPvp { get; set; } = true;

            [JsonPropertyName("recorderAudio")]
            public bool RecorderAudio { get; set; } = true;

            [JsonPropertyName("recorderMonitor")]
            public int RecorderMonitor { get; set; } = 0;

            [JsonPropertyName("eventTimersHotkey")]
            public string EventTimersHotkey { get; set; } = "Ctrl+Alt+T";


            [JsonPropertyName("bossAlertsHotkey")]
            public string BossAlertsHotkey { get; set; } = "Ctrl+Alt+B";

            [JsonPropertyName("timerRegion")]
            public string TimerRegion { get; set; } = "global-naw";

            [JsonPropertyName("autoTimerRegion")]
            public bool AutoTimerRegion { get; set; } = true;

            [JsonPropertyName("timersRiftAlert")]
            public bool TimersRiftAlert { get; set; }

            [JsonPropertyName("eventNotifications")]
            public bool EventNotifications { get; set; } = true;

            [JsonPropertyName("eventNotifySound")]
            public bool EventNotifySound { get; set; } = true;

            [JsonPropertyName("eventNotifyIds")]
            public string EventNotifyIds { get; set; } = "rift,artifact-siege,siege-bosses,guardian-nahma,watcher-kaira";

            [JsonPropertyName("bossAlerts")]
            public bool BossAlerts { get; set; } = true;

            [JsonPropertyName("bossAlertIds")]
            public string BossAlertIds { get; set; } = "watcher-kaira,guardian-nahma,siege-bosses";

            [JsonPropertyName("eventNotifyLeadMinutes")]
            public int EventNotifyLeadMinutes { get; set; } = 2;

            [JsonPropertyName("timersShowAll")]
            public bool TimersShowAll { get; set; }

            [JsonPropertyName("autoResetOnZone")]
            public bool AutoResetOnZone { get; set; } = true;

            [JsonPropertyName("clearMeterHotkey")]
            public string ClearMeterHotkey { get; set; } = "Ctrl+Alt+C";

            [JsonPropertyName("pinnedEventIds")]
            public string PinnedEventIds { get; set; } = "rift,shugo-festival";

            [JsonPropertyName("showGearScore")]
            public bool ShowGearScore { get; set; } = true;

            [JsonPropertyName("autoHideOutOfCombat")]
            public bool AutoHideOutOfCombat { get; set; } = true;


            [JsonPropertyName("fightResultPopup")]
            public bool FightResultPopup { get; set; } = true;

            [JsonPropertyName("fieldBossMapAlerts")]
            public bool FieldBossMapAlerts { get; set; } = true;
            public bool PetTracker { get; set; } = true;

            [JsonPropertyName("fieldBossAlertsHigherOnly")]
            public bool FieldBossAlertsHigherOnly { get; set; } = true;

            [JsonPropertyName("closeWithGame")]
            public bool CloseWithGame { get; set; } = true;

            [JsonPropertyName("meterOnTop")]
            public bool MeterOnTop { get; set; } = true;

            [JsonPropertyName("hideInMenus")]
            public bool HideInMenus { get; set; } = true;

            [JsonPropertyName("autoHideSeconds")]
            public int AutoHideSeconds { get; set; } = 30;

            [JsonPropertyName("showHealing")]
            public bool ShowHealing { get; set; } = false;

            [JsonPropertyName("eventDockMode")]
            public int EventDockMode { get; set; } = 1;

            [JsonPropertyName("eventDockSide")]
            public int EventDockSide { get; set; } = 0;

            [JsonPropertyName("showEventsTab")]
            public bool ShowEventsTab { get; set; } = true;

            [JsonPropertyName("highlightSelf")]
            public bool HighlightSelf { get; set; } = true;

            [JsonPropertyName("classColorsInGroup")]
            public bool ClassColorsInGroup { get; set; } = true;

            [JsonPropertyName("lockClickThrough")]
            public bool LockClickThrough { get; set; } = false;

            [JsonPropertyName("lockHotkey")]
            public string LockHotkey { get; set; } = "Ctrl+Alt+L";

            [JsonPropertyName("autoShowFightSummary")]
            public bool AutoShowFightSummary { get; set; }

            [JsonPropertyName("showFightSummary")]
            public bool ShowFightSummary { get; set; } = true;

            [JsonPropertyName("edgePartyMode")]
            public int EdgePartyMode { get; set; } = 0; // side bars off unless chosen (they popped up whenever the meter faded)

            [JsonPropertyName("recorderMaxGB")]
            public int RecorderMaxGB { get; set; } = 50;

            [JsonPropertyName("meterLayout")]
            public int MeterLayout { get; set; }

            [JsonPropertyName("bossFocus")]
            public bool BossFocus { get; set; }

            [JsonPropertyName("barBurst")]
            public bool BarBurst { get; set; } = true;

            [JsonPropertyName("barRankGlow")]
            public bool BarRankGlow { get; set; } = true;

            [JsonPropertyName("barOnlyMine")]
            public bool BarOnlyMine { get; set; }

            [JsonPropertyName("barEffect")]
            public int BarEffect { get; set; } = 1;

            [JsonPropertyName("barTheme")]
            public int BarTheme { get; set; }

            [JsonPropertyName("showPanelBackground")]
            public bool ShowPanelBackground { get; set; }

            [JsonPropertyName("showTotalDamage")]
            public bool ShowTotalDamage { get; set; } = true;

            [JsonPropertyName("historyRetantionPeriod")]
            public int HistoryRetantionPeriod { get; set; } = 30;

            [JsonPropertyName("windowOpacity")]
            public double WindowOpacity { get; set; } = 0.92;
            [JsonPropertyName("useClassColors")]
            public bool UseClassColors { get; set; }

            [JsonPropertyName("toggleVisibilityHotkey")]
            public string ToggleVisibilityHotkey { get; set; } = "Ctrl+Alt+M";

            [JsonPropertyName("networkOverlayHotkey")]
            public string NetworkOverlayHotkey { get; set; } = "Ctrl+Alt+N";

            [JsonPropertyName("windowLeft")]
            public double? WindowLeft { get; set; }

            [JsonPropertyName("windowTop")]
            public double? WindowTop { get; set; }

            [JsonPropertyName("windowWidth")]
            public double? WindowWidth { get; set; }

            [JsonPropertyName("windowHeight")]
            public double? WindowHeight { get; set; }

            [JsonPropertyName("backgroundImagePath")]
            public string? BackgroundImagePath { get; set; }

            [JsonPropertyName("relativeProgressBar")]
            public bool RelativeProgressBar { get; set; }

            [JsonPropertyName("groupSummonDamage")]
            public bool GroupSummonDamage { get; set; } = true;

            [JsonPropertyName("oneMinuteDummyMode")]
            public bool OneMinuteDummyMode { get; set; }

            [JsonPropertyName("showPlayerDeaths")]
            public bool ShowPlayerDeaths { get; set; } = true;

            [JsonPropertyName("playerRowScale")]
            public double PlayerRowScale { get; set; } = 1.0;

            [JsonPropertyName("uiStyle")]
            public int UiStyle { get; set; } = 1;

            [JsonPropertyName("statCalcCritChance")]
            public double StatCalcCritChance { get; set; } = 80;

            [JsonPropertyName("statCalcBackAttackRate")]
            public double StatCalcBackAttackRate { get; set; } = 80;

            [JsonPropertyName("statCalcFrontAttackRate")]
            public double StatCalcFrontAttackRate { get; set; } = 80;

            [JsonPropertyName("statCalcAttackType")]
            public string StatCalcAttackType { get; set; } = "Back";

            [JsonPropertyName("statCalcAttackIncreaseCombatPercent")]
            public double StatCalcAttackIncreaseCombatPercent { get; set; }

            [JsonPropertyName("statCalcPartyDamageBoost")]
            public double StatCalcPartyDamageBoost { get; set; }

            [JsonPropertyName("statCalcBossDamageTolerance")]
            public double StatCalcBossDamageTolerance { get; set; } = 30;

            [JsonPropertyName("statCalcPartySmiteBuff")]
            public double StatCalcPartySmiteBuff { get; set; }

            [JsonPropertyName("statCalcBossSmiteResist")]
            public double StatCalcBossSmiteResist { get; set; } = 30;

            [JsonPropertyName("windowBoundsByKey")]
            public Dictionary<string, WindowBounds> WindowBoundsByKey { get; set; } = [];

            [JsonPropertyName("bufOverlaySettings")]
            public OverlaySettings BufOverlaySettings { get; set; } = new OverlaySettings();

            [JsonPropertyName("skillCdOverlaySettings")]
            public OverlaySettings SkillCdOverlaySettings { get; set; } = new OverlaySettings();

        }
    }
}

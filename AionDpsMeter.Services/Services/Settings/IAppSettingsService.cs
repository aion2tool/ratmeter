using AionDpsMeter.Core.Windowing;

namespace AionDpsMeter.Services.Services.Settings
{
    public interface IAppSettingsService
    {
        bool IsPacketLoggingEnabled { get; set; }
        bool IsNicknameHidden { get; set; }
        bool BossOnlyCapture { get; set; }
        // Show damage summed over every mob hit in a pull (AoE) instead of the focused target.
        bool AllTargetsMode { get; set; }
        // Seconds without any damage that end an all-targets pull.
        int AllTargetsGapSeconds { get; set; }
        // Main meter shows total damage as the big number (DPS becomes the small one).
        bool ShowTotalDamage { get; set; }
        double OverlayScale { get; set; }
        bool ShowPanelBackground { get; set; }
        int BarTheme { get; set; }
        /// <summary>0 = plain bars, 1+ = animated bar style (lightning, charge, scanner, ... see NativeDpsOverlay.BarEffects).</summary>
        int BarEffect { get; set; }
        /// <summary>Flash at the end of a bar when that player lands a big hit.</summary>
        bool BarBurst { get; set; }
        /// <summary>Gold glow on a bar when that player moves up a rank.</summary>
        bool BarRankGlow { get; set; }
        /// <summary>Animate only your own bar (everyone else plain).</summary>
        bool BarOnlyMine { get; set; }
        /// <summary>0 = floating meter window, 1 = meter docked to the left screen edge.</summary>
        int MeterLayout { get; set; }
        /// <summary>Recordings folder size limit; oldest videos are deleted beyond it.</summary>
        int RecorderMaxGB { get; set; }
        /// <summary>Left-edge party DPS tabs: 0 off, 1 while the meter is hidden, 2 always.</summary>
        int EdgePartyMode { get; set; }
        /// <summary>Meter ignores the mouse completely (click-through).</summary>
        bool LockClickThrough { get; set; }
        /// <summary>Toggles the click-through lock.</summary>
        string LockHotkey { get; set; }
        /// <summary>Show the summary card after a boss fight.</summary>
        bool ShowFightSummary { get; set; }
        /// <summary>Show the fight card by itself after a quiet moment (false = only via the meter button).</summary>
        bool AutoShowFightSummary { get; set; }
        /// <summary>Shiny highlight on your own row when more than one player is on the meter.</summary>
        bool HighlightSelf { get; set; }
        /// <summary>Use class colours (over the theme) whenever more than one player is on the meter.</summary>
        bool ClassColorsInGroup { get; set; }
        /// <summary>Small ⏱ tab beside the meter that reopens the events drawer.</summary>
        bool ShowEventsTab { get; set; }
        /// <summary>Screen-edge event dock: 0 off, 1 while the meter is hidden, 2 always.</summary>
        int EventDockMode { get; set; }
        /// <summary>Event dock edge: 0 right, 1 left.</summary>
        int EventDockSide { get; set; }
        /// <summary>Meter shows healing done instead of damage (✚ toggle).</summary>
        bool ShowHealing { get; set; }
        /// <summary>Show each player's item level after their name (official character API).</summary>
        bool ShowGearScore { get; set; }
        /// <summary>Fade the meter out when there has been no fighting for a while.</summary>
        bool AutoHideOutOfCombat { get; set; }
        /// <summary>Fade the meter while an AION 2 menu is open (the game's cursor is free), out of combat.</summary>
        bool HideInMenus { get; set; }
        bool MeterOnTop { get; set; }
        /// <summary>Close the meter (saving fights) once the AION 2 window has been gone for a while.</summary>
        bool CloseWithGame { get; set; }
        /// <summary>Fight result card in the bottom-right corner after a boss fight (click for the summary).</summary>
        bool FightResultPopup { get; set; }
        /// <summary>Bottom-right pop-up with a map of the spot 5 min before a field boss respawns (times from the in-game map).</summary>
        bool FieldBossMapAlerts { get; set; }
        /// <summary>Pet line on the meter + level-up pop-up (souls are counted either way).</summary>
        bool PetTracker { get; set; }
        /// <summary>Field boss pop-ups only for the bosses above the map's usual level (better drops).</summary>
        bool FieldBossAlertsHigherOnly { get; set; }
        /// <summary>Seconds without damage before the meter fades out.</summary>
        int AutoHideSeconds { get; set; }
        /// <summary>Comma-separated event ids always shown as icons on the meter.</summary>
        string PinnedEventIds { get; set; }
        /// <summary>Start a fresh meter when you change zone or enter a dungeon.</summary>
        bool AutoResetOnZone { get; set; }
        /// <summary>Clears the meter.</summary>
        string ClearMeterHotkey { get; set; }
        /// <summary>Timers panel lists every event (false = only live + the next chosen event).</summary>
        bool TimersShowAll { get; set; }
        /// <summary>Show a notification before chosen events start.</summary>
        bool EventNotifications { get; set; }
        /// <summary>Play a chime with event notifications (false = muted).</summary>
        bool EventNotifySound { get; set; }
        /// <summary>Comma-separated shugo.gg event ids that notify.</summary>
        string EventNotifyIds { get; set; }
        /// <summary>Pop-up above the meter 1 h, 15 min and 3 min before chosen field bosses spawn.</summary>
        bool BossAlerts { get; set; }
        /// <summary>Comma-separated shugo.gg boss event ids that get the spawn pop-up.</summary>
        string BossAlertIds { get; set; }
        /// <summary>How many minutes before an event starts to notify.</summary>
        int EventNotifyLeadMinutes { get; set; }
        /// <summary>Shows/hides the event timers panel.</summary>
        string EventTimersHotkey { get; set; }
        /// <summary>Shows the boss pop-ups again for 10 s (they hide on their own after 10 s).</summary>
        string BossAlertsHotkey { get; set; }
        /// <summary>shugo.gg region id for event timers (global-naw, global-nae, global-eu, global-sa, global-as).</summary>
        string TimerRegion { get; set; }
        /// <summary>Set <see cref="TimerRegion"/> from your character's game server id on login (NA West / NA East).</summary>
        bool AutoTimerRegion { get; set; }
        /// <summary>Pop up the timers panel shortly before a Spacetime Rift opens.</summary>
        bool TimersRiftAlert { get; set; }
        /// <summary>Record boss fights automatically.</summary>
        bool RecorderEnabled { get; set; }
        /// <summary>Folder for boss recordings.</summary>
        string RecorderFolder { get; set; }
        /// <summary>0 = High, 1 = Balanced, 2 = Small.</summary>
        int RecorderQuality { get; set; }
        /// <summary>Also record what you hear (Windows audio loopback).</summary>
        bool RecorderAudio { get; set; }
        /// <summary>Also record PvP fights (you hit a player or a player hits you).</summary>
        bool RecorderPvp { get; set; }
        /// <summary>Desktop Duplication output index of the monitor to record.</summary>
        int RecorderMonitor { get; set; }
        /// <summary>Keep encoding a short rolling buffer so a recording includes the seconds before the fight was detected.</summary>
        bool RecorderPreRoll { get; set; }
        /// <summary>How many seconds before the fight a pre-roll recording keeps (5–60).</summary>
        int RecorderPreRollSeconds { get; set; }
        /// <summary>Auto-delete recordings older than this many days (0 = disabled).</summary>
        int RecorderRetentionDays { get; set; }
        /// <summary>Opacity of every meter window while locked (Alt held = full), 0.1–1.</summary>
        double LockedOpacity { get; set; }
        /// <summary>While locked, right-click on a row opens gear (otherwise right-clicks always reach the game).</summary>
        bool LockedRightClickGear { get; set; }
        /// <summary>Flag moments where the game server sent nothing for 2 s or more.</summary>
        bool LagStallAlert { get; set; }
        /// <summary>Show what killed you (last 10 s of damage taken) when you die.</summary>
        bool DeathRecap { get; set; }
        /// <summary>Hotkey tips under the meter (all on hover; how to unlock while locked).</summary>
        bool ShowHotkeyTips { get; set; }
        /// <summary>During a boss fight, flag a big cooldown of yours that has been ready but unused.</summary>
        bool CooldownNudge { get; set; }
        /// <summary>Big "SKILL READY" banner the moment a chosen skill comes off cooldown in combat.</summary>
        bool ReadyAlert { get; set; }
        /// <summary>Count only your own and your party members' damage (others hitting the same mob are ignored).</summary>
        bool PartyOnly { get; set; }
        /// <summary>A mob only counts once you or your party hit it (other players' fights nearby are ignored).</summary>
        bool OnlyMyFights { get; set; }
        /// <summary>Training dummy: one long session (pauses don't end it or count against DPS) until you clear the meter.</summary>
        bool DummyLongRun { get; set; }
        /// <summary>After a killed boss (not alliance fights), put the fight summary image on the clipboard.</summary>
        bool AutoCopyFightImage { get; set; }
        /// <summary>Post killed-boss results to a Discord channel (webhook) when a listed friend is in the party.</summary>
        bool DiscordShareEnabled { get; set; }
        string DiscordWebhookUrl { get; set; }
        /// <summary>Comma-separated friends' character names.</summary>
        string DiscordFriends { get; set; }
        int DiscordMinFriends { get; set; }
        /// <summary>Opens the last fight summary (◀ ▶ there for earlier ones).</summary>
        string LastFightHotkey { get; set; }
        /// <summary>Starts / stops a manual video recording.</summary>
        string RecordHotkey { get; set; }
        /// <summary>Comma-separated skill names for the ready banner (first ready one shows).</summary>
        string ReadyAlertSkills { get; set; }
        /// <summary>Field bosses picked for the map pop-up (comma-separated names). Empty: higher-level only / all.</summary>
        string FieldBossPicks { get; set; }
        /// <summary>Minutes before a field boss respawns that its pop-up shows.</summary>
        int FieldBossLeadMinutes { get; set; }
        /// <summary>One short sound each time a ready banner comes up.</summary>
        bool ReadyAlertSound { get; set; }
        /// <summary>Only skills with at least this cooldown (seconds) are nudged.</summary>
        int CooldownNudgeMinCooldown { get; set; }
        /// <summary>Seconds a skill may sit ready before the nudge shows.</summary>
        int CooldownNudgeAfter { get; set; }
        /// <summary>Skill ids the nudge never shows (clicked away on the meter).</summary>
        IReadOnlyList<long> CooldownNudgeIgnored { get; set; }
        /// <summary>Show only boss fights (view filter: all damage is still tracked).</summary>
        bool BossFocus { get; set; }
        bool GroupSummonDamage { get; set; }
        bool OneMinuteDummyMode { get; set; }
        bool ShowPlayerDeaths { get; set; }
        int HistoryRetantionPeriod { get; set; }
        double WindowOpacity { get; set; }
        string? BackgroundImagePath { get; set; }
        bool RelativeProgressBar { get; set; }

        string ToggleVisibilityHotkey { get; set; }
        string NetworkOverlayHotkey { get; set; }

        // Main window position & size
        double? WindowLeft { get; set; }
        double? WindowTop { get; set; }
        double? WindowWidth { get; set; }
        double? WindowHeight { get; set; }
        double PlayerRowScale { get; set; }
        int UiStyle { get; set; }

        public bool UseClassColors { get; set; }

        double StatCalcCritChance { get; set; }
        double StatCalcBackAttackRate { get; set; }
        double StatCalcFrontAttackRate { get; set; }
        string StatCalcAttackType { get; set; }
        double StatCalcAttackIncreaseCombatPercent { get; set; }
        double StatCalcPartyDamageBoost { get; set; }
        double StatCalcBossDamageTolerance { get; set; }
        double StatCalcPartySmiteBuff { get; set; }
        double StatCalcBossSmiteResist { get; set; }
        OverlaySettings BufOverlaySettings { get; set; }
        OverlaySettings SkillCdOverlaySettings { get; set; }
        bool TryGetWindowBounds(WindowKey key, out WindowBounds? bounds);
        void SetWindowBounds(WindowKey key, WindowBounds bounds);

        event EventHandler SettingsChanged;
    }
}

using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.Services.Services.Session
{
    /// <summary>
    /// Starts a fresh meter when you change zone or enter a dungeon. Main signal: the 0x4005 packet
    /// the game sends at the end of every loading screen (see ZoneLoadProcessor). Backup signals:
    /// your own character info arriving with a NEW entity id, or the game connection moving to a
    /// new client port.
    /// Never resets mid-fight: there must be no damage for a few seconds. Every detection is logged.
    /// </summary>
    public sealed class ZoneChangeDetector
    {
        private static readonly TimeSpan QuietBefore = TimeSpan.FromSeconds(6);  // no damage this long before resetting
        private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(15);    // one reset per loading screen

        private readonly CombatSessionManager sessions;
        private readonly IAppSettingsService settings;
        private readonly ILogger<ZoneChangeDetector> logger;
        private readonly Lock gate = new();
        private int lastUserEntity;
        private DateTime lastUserInfoAt = DateTime.MinValue;
        private DateTime lastReset = DateTime.MinValue;

        public ZoneChangeDetector(
            EntityTracker entityTracker,
            IPacketCaptureDevice captureDevice,
            CombatSessionManager sessions,
            IAppSettingsService settings,
            ILogger<ZoneChangeDetector> logger)
        {
            this.sessions = sessions;
            this.settings = settings;
            this.logger = logger;
            entityTracker.UserInfoReceived += OnUserInfo;
            if (captureDevice is CaptureDevice device) device.GameConnectionChanged += OnConnectionChanged;
        }

        private void OnUserInfo(int entityId)
        {
            lock (gate)
            {
                var now = DateTime.Now;
                bool firstSeen = lastUserEntity == 0;
                bool newEntity = !firstSeen && entityId != lastUserEntity;
                logger.LogDebug("[ZONE] Own character info received (entity {Entity}{Change}, {Gap:F0}s since last)",
                    entityId, newEntity ? ", NEW id" : "", firstSeen ? 0 : (now - lastUserInfoAt).TotalSeconds);
                lastUserEntity = entityId;
                lastUserInfoAt = now;
                if (newEntity) Trigger("new character entity id");
            }
        }

        /// <summary>Called by ZoneLoadProcessor when a loading screen finishes.</summary>
        public void OnZoneLoaded()
        {
            lock (gate)
            {
                logger.LogInformation("[ZONE] Loading screen finished (0x4005)");
                Trigger("zone loaded");
            }
        }

        private void OnConnectionChanged(int oldPort, int newPort)
        {
            lock (gate)
            {
                logger.LogInformation("[ZONE] Game connection moved from client port {Old} to {New}", oldPort, newPort);
                Trigger("new game connection");
            }
        }

        private void Trigger(string reason)
        {
            var now = DateTime.Now;
            if (now - lastReset < Debounce) return;
            if (now - sessions.LastDamageAt < QuietBefore)
            {
                logger.LogInformation("[ZONE] Zone change signal ({Reason}) ignored: combat in progress", reason);
                return;
            }
            if (!settings.AutoResetOnZone)
            {
                logger.LogInformation("[ZONE] Zone change detected ({Reason}); auto reset is off", reason);
                return;
            }
            lastReset = now;
            sessions.CompleteAndPersistActiveSessions(); // boss fights go to History first
            sessions.Reset();
            logger.LogInformation("[ZONE] Zone change detected ({Reason}): meter reset", reason);
        }
    }
}

using System.IO;
using System.Text.Json;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Timers
{
    /// <summary>
    /// Timer Region "Auto": picks NA West / NA East from your own character's game server id
    /// (faction*1000 + region*100 + server). Seen in game: NA West Israphel 2001 (region digit 0),
    /// NA East 1104 (digit 1). EU servers also send digit 0, so a manual Europe choice is never
    /// switched to NA West. Checked on every login and once at startup from last-user.json.
    /// </summary>
    public sealed class RegionAutoDetector
    {
        private readonly IAppSettingsService settings;
        private readonly ILogger<RegionAutoDetector> logger;

        public RegionAutoDetector(EntityTracker entities, IAppSettingsService settings, ILogger<RegionAutoDetector> logger)
        {
            this.settings = settings;
            this.logger = logger;
            entities.UserInfoReceived += id => Apply(entities.GetPlayerEntity(id)?.ServerId ?? 0);
            settings.SettingsChanged += (_, _) => { if (settings.AutoTimerRegion && lastServerId > 0) Apply(lastServerId); };
            Apply(ReadLastUserServerId());
        }

        private int lastServerId;

        /// <summary>"global-nae" / "global-naw" for a game server id, or null when it can't tell.</summary>
        public static string? RegionFor(int serverId) => serverId <= 0 ? null : (serverId / 100 % 10) switch
        {
            0 => "global-naw",
            1 => "global-nae",
            _ => null,
        };

        private void Apply(int serverId)
        {
            if (serverId <= 0) return;
            lastServerId = serverId;
            if (!settings.AutoTimerRegion) return;
            var region = RegionFor(serverId);
            if (region is null || region == settings.TimerRegion) return;
            if (region == "global-naw" && settings.TimerRegion == "global-eu") return;
            logger.LogInformation("[REGION] Server id {ServerId} -> timer region {Region} (was {Old})", serverId, region, settings.TimerRegion);
            settings.TimerRegion = region;
        }

        /// <summary>Game server id of the last character you logged into (faction * 1000 + server), 0 if unknown.</summary>
        internal static int ReadLastUserServerId()
        {
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "last-user.json");
                if (!File.Exists(file)) return 0;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                return doc.RootElement.TryGetProperty("ServerId", out var id) && id.TryGetInt32(out int v) ? v : 0;
            }
            catch (Exception) { return 0; }
        }
    }
}

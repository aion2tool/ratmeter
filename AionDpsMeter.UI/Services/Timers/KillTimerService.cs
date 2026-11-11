using System.IO;
using System.Text.Json;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.FieldBosses;
using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Timers
{
    /// <summary>
    /// Respawn timers for the field bosses that come back a set time after they die (no fixed schedule):
    /// Asmodian Immortal Gartua and High Commander Lagta in Altgard, 12 h on Global (couga54 AION 2 guides).
    /// A kill is recorded when the meter sees the boss die in a fight you were in, or by hand in Settings.
    /// Opening the world map overrides both with the game's exact times (the map's field boss list).
    /// Saved in %LOCALAPPDATA%\AionDpsMeter\killtimers.json so timers survive a restart.
    /// </summary>
    public sealed class KillTimerService
    {
        private static readonly string SavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "killtimers.json");
        /// <summary>After the respawn the boss is "up" (until someone kills it again).</summary>
        private static readonly TimeSpan UpFor = TimeSpan.FromHours(12);

        public static readonly IReadOnlyList<KillBoss> Bosses =
        [
            new("kill-gartua", "Immortal Gartua", "🗿", TimeSpan.FromHours(12), SpawnId: 111021),
            new("kill-lagta", "High Commander Lagta", "🪖", TimeSpan.FromHours(12), SpawnId: 111022),
        ];

        private readonly ILogger<KillTimerService> logger;
        private readonly EntityTracker entities;
        private readonly Lock gate = new();
        private readonly Dictionary<string, DateTime> kills; // boss id → last kill (UTC)
        private int serverId = RegionAutoDetector.ReadLastUserServerId();

        /// <summary>
        /// The character you're on is Asmodian (or unknown). Game server id = faction * 1000 + server:
        /// 1 = Elyos (e.g. 1104), 2 = Asmodian (e.g. 2001). These timers and the Asmo Kibelisk hints are Asmodian only.
        /// </summary>
        public bool AsmodianSide => serverId / 1000 != 1;

        /// <summary>A kill was recorded or cleared (any thread).</summary>
        public event Action? Changed;

        public KillTimerService(CombatSessionManager sessions, EntityTracker entities, FieldBossBoard fieldBosses, ILogger<KillTimerService> logger)
        {
            this.logger = logger;
            this.entities = entities;
            kills = Load();
            sessions.BossSessionCompleted += OnBossFinished;
            entities.UserInfoReceived += id => { if (entities.GetPlayerEntity(id)?.ServerId is > 0 and var s) serverId = s; };
            fieldBosses.Updated += OnFieldBossList;
        }

        /// <summary>
        /// The map's field boss list has the exact respawn time (or spawn time while up): store it as the kill
        /// time it implies, so the timer and alerts match the game to the second. Only saved when it changes.
        /// </summary>
        private void OnFieldBossList(int mapId, IReadOnlyList<FieldBossEntry> list)
        {
            foreach (var boss in Bosses)
            {
                if (list.FirstOrDefault(e => e.SpawnId == boss.SpawnId) is not { } entry) continue;
                var killed = entry.TimeUtc - boss.Respawn; // waiting: respawn - 12 h; up: spawned - 12 h
                if (LastKill(boss.Id) is { } known && Math.Abs((known - killed).TotalSeconds) < 2) continue;
                MarkKilled(boss.Id, killed);
            }
        }

        public DateTime? LastKill(string id)
        {
            lock (gate) return kills.TryGetValue(id, out var t) ? t : null;
        }

        public void MarkKilled(string id, DateTime whenUtc)
        {
            if (Bosses.All(b => b.Id != id)) return;
            lock (gate) { kills[id] = whenUtc; Save(); }
            logger.LogInformation("Kill timer: {Boss} killed at {At:u}", id, whenUtc);
            Changed?.Invoke();
        }

        public void Clear(string id)
        {
            lock (gate) { if (!kills.Remove(id)) return; Save(); }
            Changed?.Invoke();
        }

        /// <summary>Same shape as the shugo.gg events: Next = respawn while waiting, LiveStart = respawn once it's up.</summary>
        public IReadOnlyList<EventStatus> GetStatus(DateTime nowUtc)
        {
            var list = new List<EventStatus>();
            if (!AsmodianSide) return list; // Altgard bosses: nothing to farm on an Elyos character
            foreach (var b in Bosses)
            {
                if (LastKill(b.Id) is not { } killed) continue;
                var spawn = killed + b.Respawn;
                if (nowUtc - spawn > UpFor) continue; // stale
                bool up = nowUtc >= spawn;
                list.Add(new EventStatus(b.Id, b.Name, b.Icon, "boss",
                    $"Killed {killed.ToLocalTime():ddd t}; respawns {b.Respawn.TotalHours:0} h after the kill.",
                    up ? spawn : null, up ? null : spawn, UpFor, null));
            }
            return list;
        }

        private void OnBossFinished(TargetCombatSession session)
        {
            var boss = Bosses.FirstOrDefault(b => string.Equals(b.Name, session.TargetInfo.Name, StringComparison.OrdinalIgnoreCase));
            if (boss is null) return;
            var mob = entities.GetTargetMob(session.TargetInfo.Id) ?? session.TargetInfo;
            if (!mob.IsDead) return; // wipe / reset / left
            MarkKilled(boss.Id, session.LastHitTime.ToUniversalTime());
        }

        private static Dictionary<string, DateTime> Load()
        {
            try
            {
                if (File.Exists(SavePath))
                    return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(SavePath))?
                        .ToDictionary(p => p.Key, p => p.Value.ToUniversalTime()) ?? new();
            }
            catch (Exception) { /* corrupt file: start empty */ }
            return new();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                File.WriteAllText(SavePath, JsonSerializer.Serialize(kills));
            }
            catch (Exception ex) { logger.LogWarning(ex, "Saving kill timers failed"); }
        }
    }

    /// <param name="SpawnId">The boss's id in the game's map list (synced when the map is opened).</param>
    public sealed record KillBoss(string Id, string Name, string Icon, TimeSpan Respawn, int SpawnId = 0);
}

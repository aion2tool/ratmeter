using System.IO;
using System.Text.Json;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Records
{
    /// <summary>
    /// Your personal best and average DPS per boss, updated when a boss fight finishes.
    /// Only kills count (a wipe's DPS isn't a record); fights shorter than 20 s (accidental tags) don't count either. Saved in %LOCALAPPDATA%\AionDpsMeter\bests.json.
    /// </summary>
    public sealed class PersonalBestService
    {
        private static readonly TimeSpan MinFight = TimeSpan.FromSeconds(20);
        private static readonly string SavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "bests.json");

        private readonly ILogger<PersonalBestService> logger;
        private readonly EntityTracker entities;
        private readonly Lock gate = new();
        private readonly Dictionary<string, BossRecord> records;

        /// <summary>The last fight that beat your previous best (shown on the meter for a minute).</summary>
        public NewRecord? LastNewRecord { get; private set; }

        public PersonalBestService(CombatSessionManager sessions, EntityTracker entities, ILogger<PersonalBestService> logger)
        {
            this.logger = logger;
            this.entities = entities;
            records = Load();
            sessions.BossSessionCompleted += OnBossFinished;
        }

        public BossRecord? Get(string boss)
        {
            lock (gate) return records.TryGetValue(boss, out var r) ? r : null;
        }

        private void OnBossFinished(TargetCombatSession session)
        {
            if (session.GetCombatDuration() < MinFight) return;
            var mob = entities.GetTargetMob(session.TargetInfo.Id) ?? session.TargetInfo;
            if (!mob.IsDead) return; // wipe / reset / left the zone
            var me = session.GetPlayerStats().FirstOrDefault(p => p.IsUser);
            if (me is null || me.DamagePerSecond <= 0) return;
            string boss = session.TargetInfo.Name;
            if (string.IsNullOrWhiteSpace(boss)) return;

            lock (gate)
            {
                if (!records.TryGetValue(boss, out var r)) records[boss] = r = new BossRecord();
                double previous = r.BestDps;
                r.Fights++;
                r.DpsSum += me.DamagePerSecond;
                if (me.DamagePerSecond > r.BestDps)
                {
                    r.BestDps = me.DamagePerSecond;
                    r.BestTotal = me.TotalDamage;
                    r.BestAt = DateTime.Now;
                    r.BestSessionId = session.SessionId;
                    if (r.Fights > 1) // the first fight isn't a "record", just the baseline
                        LastNewRecord = new NewRecord(boss, me.DamagePerSecond, previous, DateTime.Now);
                    logger.LogInformation("Personal best on {Boss}: {Dps:F0} DPS (was {Previous:F0})", boss, me.DamagePerSecond, previous);
                }
                Save();
            }
        }

        private static Dictionary<string, BossRecord> Load()
        {
            try
            {
                if (File.Exists(SavePath))
                    return JsonSerializer.Deserialize<Dictionary<string, BossRecord>>(File.ReadAllText(SavePath)) ?? new();
            }
            catch (Exception) { /* unreadable: start fresh */ }
            return new();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                File.WriteAllText(SavePath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException ex) { logger.LogWarning("Saving personal bests failed: {Error}", ex.Message); }
        }

        public sealed class BossRecord
        {
            public double BestDps { get; set; }
            public long BestTotal { get; set; }
            public DateTime BestAt { get; set; }
            /// <summary>History id of the record fight (for "compare with best"); null for records saved before this existed.</summary>
            public Guid? BestSessionId { get; set; }
            public int Fights { get; set; }
            public double DpsSum { get; set; }
            public double AverageDps => Fights > 0 ? DpsSum / Fights : 0;
        }

        public sealed record NewRecord(string Boss, double Dps, double PreviousBest, DateTime At);
    }
}

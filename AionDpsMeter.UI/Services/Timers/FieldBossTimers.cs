using System.IO;
using System.Text.Json;
using AionDpsMeter.Services.Services.FieldBosses;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Timers
{
    /// <summary>
    /// Exact field boss respawn times from the game's own map list (sent while the world map is open), shown in
    /// the events drawer. Only spawns with a known name are listed; names come from matching a capture to the
    /// in-game list. The last list is saved so the countdowns survive a restart until the next time the map
    /// is opened. Saved in %LOCALAPPDATA%\AionDpsMeter\fieldbosses.json.
    /// </summary>
    public sealed class FieldBossTimers
    {
        private static readonly string SavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "fieldbosses.json");
        private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30);

        /// <summary>Spawn id → boss name, Altgard (map 1110), matched second-for-second to the in-game list.</summary>
        public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
        {
            [111011] = "Special Operations Leader",
            [111015] = "Executioner Barthien",
            [111016] = "Drakan Battalion Weapon…",
            [111018] = "Visionary Karuka",
            [111019] = "Dark Shadow Vishwada",
            [111020] = "Sharp Shylak",
            [111021] = "Immortal Gartua",
            [111022] = "High Commander Lagta",
            [111023] = "Soul Ruler Kashapa",
            [111024] = "Silent Dartan",
        };

        /// <summary>How long after its respawn time a boss still counts as "just spawned" (for the SPAWNED alert).</summary>
        private static readonly TimeSpan SpawnedFor = TimeSpan.FromMinutes(2);

        private readonly ILogger<FieldBossTimers> logger;
        private readonly FieldBossMaps maps;
        private readonly Lock gate = new();
        private List<FieldBossEntry> entries;
        /// <summary>Where each spawn was when first seen up (its spawn point), for placing it on the map.</summary>
        private readonly Dictionary<int, SavedPosition> positions;
        private DateTime seenAt;
        private DateTime lastSave = DateTime.MinValue;

        public FieldBossTimers(FieldBossBoard board, FieldBossMaps maps, ILogger<FieldBossTimers> logger)
        {
            this.logger = logger;
            this.maps = maps;
            (entries, seenAt, positions) = Load();
            board.Updated += OnList;
            foreach (var map in positions.Keys.GroupBy(FieldBossMaps.MapOf)) LearnMap(map.Key);
        }

        private void OnList(int mapId, IReadOnlyList<FieldBossEntry> list)
        {
            lock (gate)
            {
                entries = list.ToList();
                seenAt = DateTime.Now;
                bool moved = false;
                foreach (var e in list)
                {
                    if (!e.IsUp || e.Position is not { } p) continue;
                    if (positions.TryGetValue(e.SpawnId, out var known) && known.SpawnedUtc == e.TimeUtc) continue; // same spawn, keep its first spot
                    positions[e.SpawnId] = new SavedPosition(e.TimeUtc, p.X, p.Y, p.Z);
                    moved = true;
                }
                if (moved) LearnMap(mapId);
                if (!moved && DateTime.Now - lastSave < SaveEvery) return;
                lastSave = DateTime.Now;
                Save();
            }
        }

        private void LearnMap(int mapId) => maps.Learn(mapId, positions
            .Where(p => FieldBossMaps.MapOf(p.Key) == mapId)
            .ToDictionary(p => p.Key, p => (p.Value.X, p.Value.Y, p.Value.Z)));

        /// <summary>Boss name: the matched in-game name, else the map spot it spawns on. Null while unknown.</summary>
        public string? NameOf(int spawnId) => Names.TryGetValue(spawnId, out var name) ? name : SpotOf(spawnId)?.Name;

        /// <summary>Where a spawn is on the map, or null while unknown.</summary>
        public BossSpot? SpotOf(int spawnId)
        {
            if (maps.Find(spawnId, Names.GetValueOrDefault(spawnId)) is { } spot) return spot;
            lock (gate) return maps.FindByElimination(spawnId, entries.Select(e => e.SpawnId));
        }

        /// <summary>A higher-level field boss than the map's usual ones (the better drops).</summary>
        public bool IsAboveNormalLevel(int spawnId) => SpotOf(spawnId) is { } spot && maps.IsAboveNormalLevel(spot);

        /// <summary>Field bosses of your side's map (Verteron for Elyos, Altgard for Asmodians), strongest first.</summary>
        public IReadOnlyList<(string Name, string Level, bool Better)> BossesForSide(bool asmodian)
        {
            var spots = maps.AllSpots(asmodian ? 1110 : 1010);
            if (spots is null) return [];
            return spots.GroupBy(s => s.Name).Select(g => g.First())
                .Select(s => (s.Name, s.Level, maps.IsAboveNormalLevel(s)))
                .OrderByDescending(b => b.Item3).ThenByDescending(b => b.Level).ThenBy(b => b.Name).ToList();
        }

        /// <summary>
        /// The boss gets a map pop-up: picked by name in Settings, or with no picks, the higher-level ones (or all).
        /// </summary>
        public bool IsWanted(int spawnId, string name, string picks, bool higherOnly)
        {
            var picked = picks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (picked.Length > 0) return picked.Contains(name, StringComparer.OrdinalIgnoreCase);
            return !higherOnly || IsAboveNormalLevel(spawnId);
        }

        /// <summary>
        /// Bosses for the spawn pop-up: waiting ones (Next = exact respawn) and ones whose respawn time passed in
        /// the last 2 minutes (LiveStart). Id is "field-&lt;spawn id&gt;".
        /// </summary>
        public IReadOnlyList<EventStatus> GetAlerts(DateTime nowUtc)
        {
            lock (gate)
            {
                var list = new List<EventStatus>();
                foreach (var e in entries)
                {
                    if (e.IsUp || e.TimeUtc < nowUtc - SpawnedFor || NameOf(e.SpawnId) is not { } name) continue;
                    var spot = SpotOf(e.SpawnId);
                    string about = spot is null ? "Field boss" : $"{spot.Map} field boss · {spot.Level}".TrimEnd(' ', '·');
                    list.Add(new EventStatus($"field-{e.SpawnId}", name, "🐲", "boss", about,
                        e.TimeUtc <= nowUtc ? e.TimeUtc : null, e.TimeUtc > nowUtc ? e.TimeUtc : null, TimeSpan.Zero, null));
                }
                return list;
            }
        }

        /// <summary>Named bosses still waiting to respawn, soonest first (as drawer rows).</summary>
        public IReadOnlyList<EventStatus> GetStatus(DateTime nowUtc)
        {
            lock (gate)
            {
                return entries
                    .Where(e => !e.IsUp && e.TimeUtc > nowUtc && NameOf(e.SpawnId) is not null)
                    .OrderBy(e => e.TimeUtc)
                    .Select(e => new EventStatus($"field-{e.SpawnId}", NameOf(e.SpawnId)!, "🐲", "boss",
                        $"{(SpotOf(e.SpawnId) is { } s ? $"{s.Map} · {s.Area}" : "Field boss")} · exact respawn from the game's map list (map opened {seenAt:t}).",
                        null, e.TimeUtc, TimeSpan.Zero, null))
                    .ToList();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                File.WriteAllText(SavePath, JsonSerializer.Serialize(new Snapshot(seenAt, entries.Select(e => new SavedEntry(e.SpawnId, e.IsUp, e.TimeUtc)).ToList(), positions)));
            }
            catch (Exception ex) { logger.LogWarning(ex, "Saving field boss timers failed"); }
        }

        private static (List<FieldBossEntry>, DateTime, Dictionary<int, SavedPosition>) Load()
        {
            try
            {
                if (File.Exists(SavePath) && JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SavePath)) is { } s)
                    return (s.Entries.Select(e => new FieldBossEntry(e.SpawnId, e.IsUp, e.TimeUtc.ToUniversalTime(), null)).ToList(), s.SeenAt, s.Positions ?? []);
            }
            catch (Exception) { /* corrupt: start empty */ }
            return ([], DateTime.MinValue, []);
        }

        private sealed record Snapshot(DateTime SeenAt, List<SavedEntry> Entries, Dictionary<int, SavedPosition>? Positions);
        private sealed record SavedEntry(int SpawnId, bool IsUp, DateTime TimeUtc);
        private sealed record SavedPosition(DateTime SpawnedUtc, float X, float Y, float Z);
    }
}

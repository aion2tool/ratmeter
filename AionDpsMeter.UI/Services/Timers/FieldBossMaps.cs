using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Timers
{
    /// <summary>
    /// Where each field boss spawn is, for the map in the spawn pop-up. Boss spots and map tiles come from
    /// allthings.how (static files, downloaded once into %LOCALAPPDATA%\AionDpsMeter\maps\&lt;map&gt;). The game's
    /// boss list only gives world positions for bosses that are up, so those are fitted onto the spots
    /// (scale / rotation / offset found by trying point pairs) and every spawn id is named after the spot it
    /// sits on. Spawn ids that were never seen up fall back to a known name (Altgard) matched by name.
    /// Data: allthings.how; map art (c) NCSOFT.
    /// </summary>
    public sealed class FieldBossMaps
    {
        private const string Base = "https://static.allthings.how/game-maps/aion-2";
        private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "maps");
        /// <summary>Game map id → site map name, tile scene, native map size in px.</summary>
        private static readonly Dictionary<int, MapInfo> Maps = new()
        {
            [1010] = new("Verteron", "verteron", "world_l_a", 16384),
            [1110] = new("Altgard", "altgard", "world_d_a", 8192),
        };
        private const int Tile = 256;
        /// <summary>Tile zoom used for the pop-up: two below native, so 1 image px = 4 map px.</summary>
        private const int ZoomBelowNative = 2;

        private readonly ILogger<FieldBossMaps> logger;
        private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly Lock gate = new();
        private readonly Dictionary<int, MapState> states = new();
        private readonly Dictionary<string, BitmapSource> tiles = new();

        public FieldBossMaps(ILogger<FieldBossMaps> logger)
        {
            this.logger = logger;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AionDpsMeter");
        }

        public static int MapOf(int spawnId) => spawnId / 100;

        /// <summary>Every field boss on a map (null while the list is still downloading; asks for it).</summary>
        public IReadOnlyList<BossSpot>? AllSpots(int mapId)
        {
            if (!Maps.ContainsKey(mapId)) return null;
            lock (gate)
            {
                if (StateOf(mapId).Spots is { } spots) return spots;
            }
            _ = SpotsAsync(mapId);
            return null;
        }

        /// <summary>Positions of the spawns seen up on this map (world x, y, z). Refits in the background when new ones arrive.</summary>
        public void Learn(int mapId, IReadOnlyDictionary<int, (float X, float Y, float Z)> positions)
        {
            if (!Maps.ContainsKey(mapId)) return;
            MapState state;
            lock (gate)
            {
                state = StateOf(mapId);
                if (state.Busy || positions.Count == state.FittedCount) return;
                state.Busy = true;
            }
            var copy = positions.ToDictionary(p => p.Key, p => p.Value);
            _ = Task.Run(async () =>
            {
                try
                {
                    var spots = await SpotsAsync(mapId);
                    var placed = Fit(spots, copy, Maps[mapId].Native, out string how);
                    lock (gate)
                    {
                        state.FittedCount = copy.Count;
                        if (placed is null) return;
                        state.Placed = placed;
                        Save(mapId, state);
                    }
                    logger.LogInformation("[BOSSES] Map {Map}: placed {Count} of {Total} spawns on the map ({How})", mapId, placed.Count, copy.Count, how);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Field boss map fit failed"); }
                finally { lock (gate) state.Busy = false; }
            });
        }

        /// <summary>The spot of a spawn: fitted from the game's positions, else by name. Null until known.</summary>
        public BossSpot? Find(int spawnId, string? knownName)
        {
            int mapId = MapOf(spawnId);
            if (!Maps.ContainsKey(mapId)) return null;
            lock (gate)
            {
                var state = StateOf(mapId);
                if (state.Placed.TryGetValue(spawnId, out var spot)) return spot;
                if (knownName is null) return null;
                if (state.Spots is null) { _ = SpotsAsync(mapId); return null; } // ready on a later tick
                if (state.Spots.FirstOrDefault(s => s.Name.Equals(knownName, StringComparison.OrdinalIgnoreCase)) is { } exact) return exact;
                // Same last name only ("Immortal Gartua" vs "Eternal Gartua"), when that is unambiguous.
                string last = knownName.Split(' ')[^1];
                var same = state.Spots.Where(s => s.Name.Split(' ')[^1].Equals(last, StringComparison.OrdinalIgnoreCase)).ToList();
                return same.Select(s => s.Name).Distinct().Count() == 1 ? same[0] : null;
            }
        }

        /// <summary>
        /// A spawn never seen up: when it is the only unplaced spawn on its map and exactly one boss has no spawn yet,
        /// it must be that boss (Verteron's Eternal Gartua, until it is seen up once).
        /// </summary>
        public BossSpot? FindByElimination(int spawnId, IEnumerable<int> mapSpawnIds)
        {
            int mapId = MapOf(spawnId);
            if (!Maps.ContainsKey(mapId)) return null;
            lock (gate)
            {
                var state = StateOf(mapId);
                if (state.Spots is null) { _ = SpotsAsync(mapId); return null; } // ready on a later tick
                var unplaced = mapSpawnIds.Where(id => MapOf(id) == mapId && !state.Placed.ContainsKey(id)).Distinct().ToList();
                if (unplaced.Count != 1 || unplaced[0] != spawnId) return null;
                var used = state.Placed.Values.Select(s => s.Name).ToHashSet();
                var left = state.Spots.Where(s => !used.Contains(s.Name)).ToList();
                return left.Select(s => s.Name).Distinct().Count() == 1 ? left[0] : null;
            }
        }

        /// <summary>
        /// The boss is above the level most field bosses on its map are (Verteron: Lagta / Dartan / Kashapa 48 and
        /// Gartua 51 vs the usual 45), i.e. the ones with the better drops.
        /// </summary>
        public bool IsAboveNormalLevel(BossSpot spot)
        {
            int level = LevelOf(spot);
            if (level <= 0 || !Maps.ContainsKey(spot.MapId)) return false;
            List<int> levels;
            lock (gate)
            {
                var state = StateOf(spot.MapId);
                levels = (state.Spots ?? state.Placed.Values.ToList()).Select(LevelOf).Where(l => l > 0).ToList();
            }
            if (levels.Count == 0) return false;
            int normal = levels.GroupBy(l => l).OrderByDescending(g => g.Count()).First().Key;
            return level > normal;
        }

        private static int LevelOf(BossSpot spot) => int.TryParse(new string(spot.Level.Where(char.IsDigit).ToArray()), out int level) ? level : 0;

        /// <summary>A crop of the map centred on the spot (1 px = 4 map px), frozen so any thread can use it.</summary>
        public async Task<BitmapSource?> CropAsync(BossSpot spot, int width, int height)
        {
            if (!Maps.TryGetValue(spot.MapId, out var map)) return null;
            int zoom = (int)Math.Log2(map.Native / Tile) - ZoomBelowNative;
            double perPx = map.Native / (double)(Tile << zoom);
            double cx = spot.X / perPx, cy = spot.Y / perPx;
            int left = (int)Math.Round(cx - width / 2.0), top = (int)Math.Round(cy - height / 2.0);
            int maxTile = (1 << zoom) - 1;

            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 0xbe; pixels[i + 1] = 0xcf; pixels[i + 2] = 0xd6; pixels[i + 3] = 255; } // site sea colour
            for (int ty = Math.Max(0, top / Tile); ty <= Math.Min(maxTile, (top + height - 1) / Tile); ty++)
                for (int tx = Math.Max(0, left / Tile); tx <= Math.Min(maxTile, (left + width - 1) / Tile); tx++)
                {
                    if (await TileAsync(map, zoom, tx, ty) is not { } tile) continue;
                    var src = new byte[Tile * Tile * 4];
                    tile.CopyPixels(src, Tile * 4, 0);
                    for (int y = 0; y < Tile; y++)
                    {
                        int dy = ty * Tile + y - top;
                        if (dy < 0 || dy >= height) continue;
                        int x0 = Math.Max(0, left - tx * Tile), x1 = Math.Min(Tile, left + width - tx * Tile);
                        if (x1 <= x0) continue;
                        Buffer.BlockCopy(src, (y * Tile + x0) * 4, pixels, (dy * width + tx * Tile + x0 - left) * 4, (x1 - x0) * 4);
                    }
                }
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bmp.Freeze();
            return bmp;
        }

        private async Task<BitmapSource?> TileAsync(MapInfo map, int z, int x, int y)
        {
            string key = $"{map.Scene}/{z}/{x}/{y}";
            lock (gate) if (tiles.TryGetValue(key, out var cached)) return cached;
            var file = Path.Combine(Root, map.Site, "tiles", z.ToString(), $"{x}_{y}.webp");
            try
            {
                if (!File.Exists(file))
                {
                    using var res = await http.GetAsync($"{Base}/tiles/{key}.webp");
                    if (!res.IsSuccessStatusCode) return null; // open sea has no tile
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllBytesAsync(file, await res.Content.ReadAsByteArrayAsync());
                }
                using var stream = File.OpenRead(file);
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                BitmapSource bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                if (bgra.PixelWidth != Tile || bgra.PixelHeight != Tile) return null;
                bgra.Freeze();
                lock (gate) { if (tiles.Count > 200) tiles.Clear(); tiles[key] = bgra; }
                return bgra;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Map tile {Key} unavailable", key);
                return null;
            }
        }

        /// <summary>Field boss spots (the site's "boss-" and "named-" points) with their area names; cached on disk.</summary>
        private async Task<IReadOnlyList<BossSpot>> SpotsAsync(int mapId)
        {
            var map = Maps[mapId];
            lock (gate) if (StateOf(mapId).Spots is { } ready) return ready;
            var file = Path.Combine(Root, map.Site, "spots.json");
            List<BossSpot>? spots = null;
            try { if (File.Exists(file)) spots = JsonSerializer.Deserialize<List<BossSpot>>(await File.ReadAllTextAsync(file)); }
            catch (Exception) { /* corrupt: download again */ }
            if (spots is null || spots.Count == 0)
            {
                // search.json rows: [name, slug, area id (-1 = the name row), area name]
                using var search = JsonDocument.Parse(await http.GetStringAsync($"{Base}/{map.Site}/search.json"));
                var rows = search.RootElement.EnumerateArray().Select(r => (Name: r[0].GetString()!, Slug: r[1].GetString()!, Area: r[2].GetInt32(), AreaName: r[3].GetString() ?? "")).ToList();
                var areaNames = rows.Where(r => r.Area >= 0).GroupBy(r => r.Area).ToDictionary(g => g.Key, g => g.First().AreaName);
                spots = [];
                foreach (var (name, slug) in rows.Where(r => r.Slug.StartsWith("boss-") || r.Slug.StartsWith("named-")).Select(r => (r.Name, r.Slug)).Distinct())
                {
                    using var points = JsonDocument.Parse(await http.GetStringAsync($"{Base}/{map.Site}/points/{slug}.json"));
                    foreach (var p in points.RootElement.EnumerateArray()) // [x, y, area id, {t: "Level 48"}]
                    {
                        int area = p.GetArrayLength() > 2 ? p[2].GetInt32() : -1;
                        string level = p.GetArrayLength() > 3 && p[3].TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";
                        spots.Add(new BossSpot(name, areaNames.GetValueOrDefault(area, ""), map.Display, level, mapId, p[0].GetSingle(), p[1].GetSingle()));
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(spots));
                logger.LogInformation("[BOSSES] Downloaded {Count} field boss spots for {Map}", spots.Count, map.Display);
            }
            lock (gate) StateOf(mapId).Spots = spots;
            return spots;
        }

        /// <summary>
        /// Finds the world → map transform that puts the most spawn positions on a boss spot and names each spawn
        /// after its spot. Tries every world plane (xy / xz / yz), mirrored or not, and each pair of positions
        /// against each pair of spots. Null when no fit puts at least 5 (and 70%) of the positions on spots.
        /// </summary>
        internal static Dictionary<int, BossSpot>? Fit(IReadOnlyList<BossSpot> spots, IReadOnlyDictionary<int, (float X, float Y, float Z)> positions, int native, out string how)
        {
            how = "not enough spawns seen up yet";
            if (positions.Count < 5 || spots.Count < 2) return null;
            double tol = native * 0.015;
            var ids = positions.Keys.ToArray();
            var sx = spots.Select(s => (double)s.X).ToArray();
            var sy = spots.Select(s => (double)s.Y).ToArray();
            int need = Math.Max(5, (int)Math.Ceiling(ids.Length * 0.7));

            (int Count, double Err, double Ar, double Ai, double Tx, double Ty, int Plane, bool Mirror) best = (0, double.MaxValue, 0, 0, 0, 0, 0, false);
            var used = new int[spots.Count]; int stamp = 0;
            var seeds = ids.Take(7).ToArray(); // pairs from a few positions; every position is scored
            for (int plane = 0; plane < 3; plane++)
                for (int m = 0; m < 2; m++)
                {
                    bool mirror = m == 1;
                    var px = new double[ids.Length]; var py = new double[ids.Length];
                    for (int i = 0; i < ids.Length; i++) (px[i], py[i]) = Project(positions[ids[i]], plane, mirror);
                    for (int a = 0; a < seeds.Length; a++)
                        for (int b = a + 1; b < seeds.Length; b++)
                        {
                            double dpx = px[b] - px[a], dpy = py[b] - py[a];
                            double dd = dpx * dpx + dpy * dpy;
                            if (dd < 1e-6) continue;
                            for (int k = 0; k < spots.Count; k++)
                                for (int l = 0; l < spots.Count; l++)
                                {
                                    if (k == l) continue;
                                    double dqx = sx[l] - sx[k], dqy = sy[l] - sy[k];
                                    if (dqx * dqx + dqy * dqy < tol * tol) continue; // two spots on top of each other say nothing about scale
                                    // q = A·p + T with A complex (scale + rotation)
                                    double ar = (dqx * dpx + dqy * dpy) / dd, ai = (dqy * dpx - dqx * dpy) / dd;
                                    double tx = sx[k] - (ar * px[a] - ai * py[a]), ty = sy[k] - (ai * px[a] + ar * py[a]);
                                    // Each spot counts once, so a fit that squashes every position onto one spot scores 1.
                                    int count = 0; double err = 0; stamp++;
                                    for (int i = 0; i < ids.Length; i++)
                                    {
                                        double qx = ar * px[i] - ai * py[i] + tx, qy = ai * px[i] + ar * py[i] + ty;
                                        double nearest = double.MaxValue; int at = -1;
                                        for (int s = 0; s < sx.Length; s++)
                                        {
                                            double ex = sx[s] - qx, ey = sy[s] - qy, d = ex * ex + ey * ey;
                                            if (d < nearest) { nearest = d; at = s; }
                                        }
                                        if (nearest > tol * tol || used[at] == stamp) continue;
                                        used[at] = stamp; count++; err += Math.Sqrt(nearest);
                                    }
                                    if (count > best.Count || count == best.Count && err < best.Err)
                                        best = (count, err, ar, ai, tx, ty, plane, mirror);
                                }
                        }
                }
            how = $"{best.Count} of {ids.Length} on a spot, plane {best.Plane}{(best.Mirror ? " mirrored" : "")}, scale {Math.Sqrt(best.Ar * best.Ar + best.Ai * best.Ai):G3}";
            if (best.Count < need) return null;

            var placed = new Dictionary<int, BossSpot>();
            foreach (var id in ids)
            {
                var (x, y) = Project(positions[id], best.Plane, best.Mirror);
                double qx = best.Ar * x - best.Ai * y + best.Tx, qy = best.Ai * x + best.Ar * y + best.Ty;
                var spot = spots.MinBy(s => (s.X - qx) * (s.X - qx) + (s.Y - qy) * (s.Y - qy))!;
                if ((spot.X - qx) * (spot.X - qx) + (spot.Y - qy) * (spot.Y - qy) <= tol * tol) placed[id] = spot;
            }
            return placed;
        }

        private static (double, double) Project((float X, float Y, float Z) p, int plane, bool mirror)
        {
            var (a, b) = plane switch { 0 => (p.X, p.Y), 1 => (p.X, p.Z), _ => (p.Y, p.Z) };
            return (a, mirror ? -b : b);
        }

        private MapState StateOf(int mapId)
        {
            if (states.TryGetValue(mapId, out var s)) return s;
            s = new MapState();
            try
            {
                var file = Path.Combine(Root, Maps[mapId].Site, "placed.json");
                if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<int, BossSpot>>(File.ReadAllText(file)) is { } placed) s.Placed = placed;
            }
            catch (Exception) { /* corrupt: refit */ }
            return states[mapId] = s;
        }

        private void Save(int mapId, MapState state)
        {
            try
            {
                var file = Path.Combine(Root, Maps[mapId].Site, "placed.json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(state.Placed));
            }
            catch (Exception ex) { logger.LogWarning(ex, "Saving field boss spots failed"); }
        }

        private sealed record MapInfo(string Display, string Site, string Scene, int Native);

        private sealed class MapState
        {
            public IReadOnlyList<BossSpot>? Spots;
            public Dictionary<int, BossSpot> Placed = new();
            public int FittedCount;
            public bool Busy;
        }
    }

    /// <param name="X">Map px on the site's native map.</param>
    public sealed record BossSpot(string Name, string Area, string Map, string Level, int MapId, float X, float Y);
}

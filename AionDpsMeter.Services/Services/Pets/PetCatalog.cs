using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDpsMeter.Services.Services.Pets
{
    public sealed record PetSource(string Monster, string Level, double Chance, string[] Places);
    public sealed record PetInfo(string Name, string Family, string Url, int[] SoulItemIds, PetSource[] Sources);

    public sealed class PetCatalogData
    {
        public DateTime BuiltAt { get; set; }
        public string ClientVersion { get; set; } = "";
        public List<PetInfo> Pets { get; set; } = [];
    }

    /// <summary>Which monsters drop which pet's souls, from aion2data.com's datasets (read from the game client).</summary>
    public sealed partial class PetCatalog
    {
        public static readonly PetCatalog Empty = new(new PetCatalogData());

        private readonly Dictionary<int, PetInfo> bySoul = new();
        private readonly Dictionary<string, List<PetInfo>> byMonster = new(StringComparer.OrdinalIgnoreCase);

        public PetCatalogData Data { get; }
        public IReadOnlyList<PetInfo> Pets => Data.Pets;

        public PetCatalog(PetCatalogData data)
        {
            Data = data;
            foreach (var pet in data.Pets)
            {
                foreach (var id in pet.SoulItemIds) bySoul[id] = pet;
                foreach (var s in pet.Sources)
                {
                    if (!byMonster.TryGetValue(s.Monster, out var list)) byMonster[s.Monster] = list = [];
                    if (!list.Contains(pet)) list.Add(pet);
                }
            }
        }

        public bool IsSoul(int itemId) => bySoul.ContainsKey(itemId);
        public PetInfo? PetForSoul(int itemId) => bySoul.GetValueOrDefault(itemId);
        public IReadOnlyList<PetInfo> PetsForMonster(string monster) =>
            byMonster.TryGetValue(monster ?? "", out var list) ? list : [];

        public static string PetNameFromSoul(string? item)
        {
            var m = SoulRegex().Match(item ?? "");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        public static PetCatalogData Build(string monstersJson, string markersJson, DateTime now)
        {
            using var monsters = JsonDocument.Parse(monstersJson);
            using var markers = JsonDocument.Parse(markersJson);

            var family = new Dictionary<string, (string Family, string Url)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Records(markers))
                if (Str(r, "layer") == "pets" && Str(r, "name") is { Length: > 0 } name && !family.ContainsKey(name))
                    family[name] = (Str(r, "type"), Str(r, "url"));

            var souls = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            var sources = new Dictionary<string, List<PetSource>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Records(monsters))
            {
                if (!m.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Array) continue;
                foreach (var d in drops.EnumerateArray())
                {
                    string pet = PetNameFromSoul(Str(d, "item"));
                    if (pet.Length == 0 || !d.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out int id)) continue;
                    if (!souls.TryGetValue(pet, out var ids)) { souls[pet] = ids = []; sources[pet] = []; }
                    ids.Add(id);
                    double chance = d.TryGetProperty("chance", out var c) && c.TryGetDouble(out var v) ? v : 0;
                    string monster = Str(m, "name");
                    if (monster.Length > 0 && !sources[pet].Any(s => s.Monster.Equals(monster, StringComparison.OrdinalIgnoreCase)))
                        sources[pet].Add(new PetSource(monster, LevelText(m), chance, Places(m)));
                }
            }

            var pets = souls.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(name =>
            {
                var (fam, url) = family.TryGetValue(name, out var f) ? f : ("", "");
                return new PetInfo(name,
                    fam.Length > 0 ? fam : "Other",
                    url.Length > 0 ? url : $"https://aion2data.com/go/{Uri.EscapeDataString(name)}",
                    souls[name].Order().ToArray(),
                    sources[name].ToArray());
            }).ToList();

            return new PetCatalogData
            {
                BuiltAt = now,
                ClientVersion = monsters.RootElement.TryGetProperty("clientVersion", out var cv) ? cv.GetString() ?? "" : "",
                Pets = pets,
            };
        }

        private static IEnumerable<JsonElement> Records(JsonDocument doc) =>
            doc.RootElement.TryGetProperty("records", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray() : [];

        private static string LevelText(JsonElement m)
        {
            if (!m.TryGetProperty("level", out var l)) return "";
            List<int> nums = l.ValueKind == JsonValueKind.Array
                ? l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList()
                : l.ValueKind == JsonValueKind.Number ? [l.GetInt32()] : [];
            if (nums.Count == 0) return "";
            int lo = nums.Min(), hi = nums.Max();
            return lo == hi ? $"{lo}" : $"{lo}–{hi}";
        }

        private static string[] Places(JsonElement m)
        {
            if (!m.TryGetProperty("spawns", out var s)) return [];
            List<JsonElement> items = s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().ToList() : s.ValueKind == JsonValueKind.Object ? [s] : [];
            return items.Select(x => $"{Str(x, "map")} · {Str(x, "area")}").Where(p => p.Length > 3).Distinct().ToArray();
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        [GeneratedRegex(@"^Soul:\s*(.+?)(\s*\((Bound|Unbound)\))?$")]
        private static partial Regex SoulRegex();
    }
}

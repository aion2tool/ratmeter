using System.Collections.Frozen;
using System.Text.Json;
using AionDpsMeter.Core.GameData.Contracts;

namespace AionDpsMeter.Core.GameData.Repositories
{
   
    public sealed class MobRepository
    {
        private FrozenDictionary<int, MobData> mobsById = FrozenDictionary<int, MobData>.Empty;
        private FrozenSet<string> dummyNames = FrozenSet<string>.Empty;

       

        public void Load(string path)
        {
            if (!File.Exists(path))
                return; 

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<Dictionary<int, MobData>>(json, options);
            if (raw is not null)
            {
                mobsById = raw.ToFrozenDictionary();
                dummyNames = raw.Values.Where(m => m.isDummy).Select(m => m.Name).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        public bool IsKnownMob(int mobId) => mobsById.ContainsKey(mobId);

        public string GetName(int mobId) =>
            mobsById.TryGetValue(mobId, out var mob) ? mob.Name : $"Unknown ({mobId})";

        public bool IsBoss(int mobId) =>
            mobsById.TryGetValue(mobId, out var mob) && mob.IsBoss;

        /// <summary>Training dummies by name (saved fights keep the name, not the mob code).</summary>
        public bool IsDummyName(string name) => dummyNames.Contains(name);

        public bool IsDummy(int mobId) =>
          mobsById.TryGetValue(mobId, out var mob) && mob.isDummy;
    }
}

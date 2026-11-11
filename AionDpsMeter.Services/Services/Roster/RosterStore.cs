using System.Text.Json;

namespace AionDpsMeter.Services.Services.Roster
{
    public static class RosterStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static RosterData Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new RosterData();
                var d = JsonSerializer.Deserialize<RosterData>(File.ReadAllText(path), Json) ?? new RosterData();
                d.Characters = new Dictionary<string, CharacterWeek>(d.Characters, StringComparer.OrdinalIgnoreCase);
                if (d.TaskDefs.Count == 0) d.TaskDefs = TaskDef.Defaults();
                return d;
            }
            catch (Exception)
            {
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
                return new RosterData();
            }
        }

        public static RosterData Clone(RosterData data)
        {
            var d = JsonSerializer.Deserialize<RosterData>(JsonSerializer.Serialize(data, Json), Json)!;
            d.Characters = new Dictionary<string, CharacterWeek>(d.Characters, StringComparer.OrdinalIgnoreCase);
            return d;
        }

        public static void Save(string path, RosterData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
            File.Move(tmp, path, overwrite: true);
        }
    }
}

using System.Text.Json;

namespace AionDpsMeter.Services.Services.Pets
{
    public static class PetStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static PetData Load(string path) => Load(path, out _);

        /// <summary>
        /// Bad JSON → empty data, file kept as .bad. A file that can't be read right now (locked by antivirus,
        /// no access) → empty data with <paramref name="readFailed"/> set: the caller must not save over it.
        /// </summary>
        public static PetData Load(string path, out bool readFailed)
        {
            readFailed = false;
            string text;
            try
            {
                if (!File.Exists(path)) return new PetData();
                text = File.ReadAllText(path);
            }
            catch (Exception) { readFailed = true; return new PetData(); }

            try
            {
                var d = JsonSerializer.Deserialize<PetData>(text, Json) ?? new PetData();
                d.Pets = new Dictionary<string, PetProgress>(d.Pets ?? [], StringComparer.OrdinalIgnoreCase);
                return d;
            }
            catch (Exception)
            {
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (Exception) { }
                return new PetData();
            }
        }

        public static PetData Clone(PetData data)
        {
            var d = JsonSerializer.Deserialize<PetData>(JsonSerializer.Serialize(data, Json), Json)!;
            d.Pets = new Dictionary<string, PetProgress>(d.Pets, StringComparer.OrdinalIgnoreCase);
            return d;
        }

        public static void Save(string path, PetData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
            File.Move(tmp, path, overwrite: true);
        }
    }
}

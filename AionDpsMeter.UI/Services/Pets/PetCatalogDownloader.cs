using System.IO;
using System.Net.Http;
using System.Text.Json;
using AionDpsMeter.Services.Services.Pets;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Pets
{
    /// <summary>
    /// Which monsters drop which pet souls: built from aion2data.com's public datasets (read from the game
    /// client), cached in %LOCALAPPDATA%\AionDpsMeter\pets\catalog.json and refreshed once a week. Only
    /// downloads; nothing about the user is sent. On failure the last good copy stays in use.
    /// </summary>
    public sealed class PetCatalogDownloader(ILogger<PetCatalogDownloader> logger)
    {
        private const string MonstersUrl = "https://aion2data.com/raw/monsters.json";
        private const string MarkersUrl = "https://aion2data.com/raw/map-markers.json";
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
        private static readonly string CatalogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "pets", "catalog.json");
        private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private int started;

        public PetCatalog Current { get; private set; } = PetCatalog.Empty;
        public DateTime? LoadedAt => Current.Pets.Count > 0 ? Current.Data.BuiltAt : null;
        public string? LastError { get; private set; }
        public event Action? Updated;

        public void Start()
        {
            if (Interlocked.Exchange(ref started, 1) == 1) return;
            try
            {
                if (File.Exists(CatalogPath))
                {
                    var data = JsonSerializer.Deserialize<PetCatalogData>(File.ReadAllText(CatalogPath));
                    if (data is { Pets.Count: > 0 }) Current = new PetCatalog(data);
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "[PET] cached catalog unreadable"); }

            if (Current.Pets.Count == 0 || DateTime.UtcNow - Current.Data.BuiltAt > MaxAge)
                _ = Task.Run(RefreshAsync);
        }

        private async Task RefreshAsync()
        {
            try
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AION2-DPS-Meter (pet tracker)");
                var monsters = await http.GetStringAsync(MonstersUrl);
                var markers = await http.GetStringAsync(MarkersUrl);
                var data = PetCatalog.Build(monsters, markers, DateTime.UtcNow);
                if (data.Pets.Count < 50) throw new InvalidDataException($"only {data.Pets.Count} pets in the datasets");
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
                var tmp = CatalogPath + ".tmp";
                await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(data));
                File.Move(tmp, CatalogPath, overwrite: true);
                Current = new PetCatalog(data);
                LastError = null;
                logger.LogInformation("[PET] catalog: {Pets} pets (client {Version})", data.Pets.Count, data.ClientVersion);
                Updated?.Invoke();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                logger.LogWarning(ex, "[PET] catalog download failed, keeping the cached one");
                Updated?.Invoke();
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDpsMeter.UI.Services.Characters
{
    /// <summary>
    /// Opens a player's character page (gear) on the official AION 2 site in the browser.
    /// Finds the character by exact name + faction + server; if that isn't exactly one match,
    /// opens the site's search for the name instead of guessing.
    /// </summary>
    public static partial class GearProfile
    {
        private const string SearchUrl = "https://api-search.plaync.com/aion2global/search/v2/character";
        private const string SiteBase = "https://aion2.plaync.com/en-us/characters";
        private static readonly string[] Regions = ["naw", "nae", "eu", "la", "as"];

        private static readonly HttpClient Http = CreateClient();
        private static readonly ConcurrentDictionary<string, string> Found = new(StringComparer.OrdinalIgnoreCase);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (AION2 DPS meter)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        /// <param name="gameServerId">Server id as the game sends it (faction*1000 + server); 0 if unknown.</param>
        public static async Task OpenAsync(string name, int gameServerId)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string key = $"{name}|{gameServerId}";
            if (!Found.TryGetValue(key, out var url))
            {
                url = await FindProfileUrlAsync(name, gameServerId) ?? $"{SiteBase}/index?keyword={Uri.EscapeDataString(name)}";
                if (!url.Contains("/index?")) Found[key] = url;
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { /* no browser registered */ }
        }

        private static async Task<string?> FindProfileUrlAsync(string name, int gameServerId)
        {
            var searches = Regions.Select(region => SearchRegionAsync(name, region));
            var matches = (await Task.WhenAll(searches)).SelectMany(r => r)
                .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (gameServerId > 0)
            {
                // Site ids are faction*1000 + region*100 + server. NA East game ids match exactly (1104);
                // NA West/EU game ids carry 0 as the region digit (2001 = site 2201 naw / 2301 eu).
                var exact = matches.Where(c => c.ServerId == gameServerId).ToList();
                if (exact.Count > 0) matches = exact;
                else
                {
                    string[] regions = gameServerId / 100 % 10 == 0 ? ["naw", "eu"] : [];
                    matches = matches.Where(c => c.ServerId / 1000 == gameServerId / 1000 &&
                                                 c.ServerId % 100 == gameServerId % 100 &&
                                                 regions.Contains(c.Region)).ToList();
                }
            }
            if (matches.Count != 1) return null; // none, or several namesakes we can't tell apart
            var m = matches[0];
            return $"{SiteBase}/{m.ServerId}/{m.CharacterId}?region={m.Region}";
        }

        private sealed record Hit(string CharacterId, string Name, int ServerId, string Region);

        private static async Task<List<Hit>> SearchRegionAsync(string name, string region)
        {
            var url = $"{SearchUrl}?keyword={Uri.EscapeDataString(name)}&page=1&size=50&localeInfo=en-US&region={region}&serverId=";
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var list = new List<Hit>();
                if (!doc.RootElement.TryGetProperty("list", out var items) || items.ValueKind != JsonValueKind.Array) return list;
                foreach (var item in items.EnumerateArray())
                {
                    list.Add(new Hit(
                        Str(item, "characterId"), // already URL-encoded, as the site's own links use it
                        TagRegex().Replace(Str(item, "name"), ""),
                        item.TryGetProperty("serverId", out var s) && s.TryGetInt32(out var sid) ? sid : 0,
                        Str(item, "region") is { Length: > 0 } r ? r : region));
                }
                return list;
            }
            catch (Exception)
            {
                return []; // one region failing shouldn't hide the others
            }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex TagRegex();
    }
}

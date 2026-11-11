using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Services.Services.Discord;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Views;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Records
{
    /// <summary>
    /// Posts the boss result picture to a friends' Discord channel through its webhook (Settings → Notifications).
    /// Only kills with a listed friend in the party; failures are logged, never shown mid-game.
    /// </summary>
    public sealed class DiscordPoster(IAppSettingsService settings, ILogger<DiscordPoster> logger)
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>UI thread (renders the card). Posts if the rule says so.</summary>
        public void OnFight(FightSummary summary)
        {
            var party = summary.Party ?? summary.Rows.Select(r => r.Name).ToList();
            bool dummy = GameDataProvider.Instance.Mobs.IsDummyName(summary.Boss);
            if (!DiscordShareRule.ShouldPost(settings.DiscordShareEnabled, settings.DiscordWebhookUrl, summary.Killed, summary.Alliance, dummy,
                    party, settings.DiscordFriends, settings.DiscordMinFriends)) return;
            var friends = DiscordShareRule.FriendsPresent(party, settings.DiscordFriends);
            string text = $"**{summary.Boss}** down in {(int)summary.Duration.TotalMinutes}:{summary.Duration.Seconds:00} · with {string.Join(", ", friends)}";
            _ = SendAsync(settings.DiscordWebhookUrl, text, Png(summary));
        }

        /// <summary>Settings "Send test": a text-only message. Returns null on success, else the error.</summary>
        public async Task<string?> SendTestAsync()
        {
            if (!DiscordShareRule.IsWebhookUrl(settings.DiscordWebhookUrl)) return "That isn't a Discord webhook URL (https://discord.com/api/webhooks/…)";
            return await SendAsync(settings.DiscordWebhookUrl, "RATmeter connected: boss kills with friends will be posted here.", null);
        }

        private byte[] Png(FightSummary summary)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(FightSummaryWindow.RenderImage(summary, settings.OverlayScale)));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private async Task<string?> SendAsync(string url, string text, byte[]? png)
        {
            try
            {
                using var form = new MultipartFormDataContent();
                var payload = JsonSerializer.Serialize(new { content = text, allowed_mentions = new { parse = Array.Empty<string>() } });
                form.Add(new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), "payload_json");
                if (png is not null)
                {
                    var file = new ByteArrayContent(png);
                    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    form.Add(file, "files[0]", "result.png");
                }
                using var resp = await Http.PostAsync(url, form).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) { logger.LogInformation("Posted to Discord: {Text}", text); return null; }
                var err = $"Discord answered {(int)resp.StatusCode} {resp.ReasonPhrase}";
                logger.LogWarning("Discord post failed: {Error}", err);
                return err;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord post failed");
                return ex.Message;
            }
        }
    }
}

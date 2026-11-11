namespace AionDpsMeter.Services.Services.Discord
{
    /// <summary>
    /// When a boss result goes to the friends' Discord channel: a kill (not an alliance field boss or the
    /// training dummy) with at least <c>min</c> of the listed friends' characters in the party.
    /// </summary>
    public static class DiscordShareRule
    {
        public static IReadOnlyList<string> ParseNames(string? csv) =>
            (csv ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The listed friends who were in the fight, in party order.</summary>
        public static IReadOnlyList<string> FriendsPresent(IEnumerable<string> party, string? friendsCsv)
        {
            var friends = new HashSet<string>(ParseNames(friendsCsv), StringComparer.OrdinalIgnoreCase);
            return party.Where(p => friends.Contains(p.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool ShouldPost(bool enabled, string? webhookUrl, bool killed, bool alliance, bool dummy,
            IEnumerable<string> party, string? friendsCsv, int min) =>
            enabled && IsWebhookUrl(webhookUrl) && killed && !alliance && !dummy
            && FriendsPresent(party, friendsCsv).Count >= Math.Max(1, min);

        public static bool IsWebhookUrl(string? url) =>
            Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
            && (u.Host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com")
            && u.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);
    }
}

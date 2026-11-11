using AionDpsMeter.Services.Services.Discord;
using Xunit;

namespace AionDpsMeter.Tests
{
    public class DiscordShareRuleTests
    {
        private const string Url = "https://discord.com/api/webhooks/123/abc";
        private const string Friends = "FriendA, FriendB, FriendC";

        private static bool Post(string[] party, bool killed = true, bool alliance = false, bool dummy = false, int min = 1,
            string? url = Url, bool enabled = true) =>
            DiscordShareRule.ShouldPost(enabled, url, killed, alliance, dummy, party, Friends, min);

        [Fact] public void OneFriendInParty_Posts() => Assert.True(Post(["Hero", "friendA", "Stranger"]));
        [Fact] public void OnlyMyCharacters_DoesNotPost() => Assert.False(Post(["Hero", "AltTwo"]));
        [Fact] public void Wipe_DoesNotPost() => Assert.False(Post(["Hero", "FriendB"], killed: false));
        [Fact] public void Alliance_DoesNotPost() => Assert.False(Post(["Hero", "FriendB"], alliance: true));
        [Fact] public void Dummy_DoesNotPost() => Assert.False(Post(["Hero", "FriendB"], dummy: true));
        [Fact] public void Disabled_DoesNotPost() => Assert.False(Post(["Hero", "FriendB"], enabled: false));
        [Fact] public void MinTwo_NeedsTwoFriends()
        {
            Assert.False(Post(["Hero", "FriendB"], min: 2));
            Assert.True(Post(["Hero", "FriendB", "FriendC"], min: 2));
        }
        [Fact] public void SameFriendTwice_CountsOnce() => Assert.False(Post(["FriendB", "FriendB"], min: 2));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("http://discord.com/api/webhooks/1/a")]
        [InlineData("https://evil.com/api/webhooks/1/a")]
        [InlineData("https://discord.com/channels/1/2")]
        public void BadUrl_DoesNotPost(string? url) => Assert.False(Post(["FriendB"], url: url));
    }
}

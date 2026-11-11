using System.Windows;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Views;

namespace AionDpsMeter.UI.Services.Records
{
    /// <summary>Finds the fight to compare against (your best on that boss, or your previous fight on it) and opens the compare card.</summary>
    public sealed class FightCompareService(CombatSessionManager sessions, PersonalBestService bests, IAppSettingsService settings)
    {
        /// <returns>Null when it opened, otherwise why it couldn't (shown to the user).</returns>
        public string? CompareWithBest(Guid sessionId, Window? near = null)
        {
            var now = sessions.GetHistorySession(sessionId);
            if (now is null) return "This fight isn't in History.";
            var record = bests.Get(now.TargetName);
            if (record?.BestSessionId is not { } bestId)
                return "No personal best with a saved fight on this boss yet (bests count kills of 20 s or more).";
            if (bestId == sessionId) return "This fight is your personal best.";
            var best = sessions.GetHistorySession(bestId);
            if (best is null) return "Your best fight is no longer in History (retention).";
            return Open(now, best, "Best", near);
        }

        public string? CompareWithPrevious(Guid sessionId, Window? near = null)
        {
            var now = sessions.GetHistorySession(sessionId);
            if (now is null) return "This fight isn't in History.";
            var previous = sessions.GetHistoryPage(new HistorySessionQuery { BossNameContains = now.TargetName, PageSize = 200 }).Items
                .Where(i => i.TargetName == now.TargetName && i.SessionId != sessionId && i.SessionEnd < now.SessionEnd)
                .OrderByDescending(i => i.SessionEnd)
                .FirstOrDefault();
            if (previous is null) return "No earlier fight on this boss in History.";
            var other = sessions.GetHistorySession(previous.SessionId);
            return other is null ? "The earlier fight couldn't be loaded." : Open(now, other, "Previous", near);
        }

        private string? Open(HistorySessionSnapshot now, HistorySessionSnapshot reference, string label, Window? near)
        {
            // Compare the same person in both fights: you if you were there, else the top player of this fight.
            var a = FightAnalysis.From(now);
            if (a is null) return "No player damage in this fight.";
            var me = now.PlayerStats.FirstOrDefault(p => p.IsUser);
            long? referenceId = me is not null
                ? reference.PlayerStats.FirstOrDefault(p => p.IsUser)?.PlayerId
                : reference.PlayerStats.FirstOrDefault(p => p.PlayerName == a.PlayerName)?.PlayerId;
            var b = referenceId is { } id ? FightAnalysis.From(reference, id) : null;
            if (b is null) return $"{a.PlayerName} wasn't in the {label.ToLowerInvariant()} fight.";
            new FightCompareWindow(a, b, label, near, settings.OverlayScale).Show();
            return null;
        }
    }
}

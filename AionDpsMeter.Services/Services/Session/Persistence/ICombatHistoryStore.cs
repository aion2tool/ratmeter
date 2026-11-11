namespace AionDpsMeter.Services.Services.Session.Persistence
{
    public interface ICombatHistoryStore
    {
        void Save(HistorySessionSnapshot snapshot);
        void FlushPendingSaves();
        IReadOnlyList<HistorySessionListItem> GetSessionList();
        int GetSessionCount(DateTime? dateFrom, DateTime? dateTo, string? bossNameContains, IReadOnlySet<Guid>? excludeSessionIds = null);
        IReadOnlyList<HistorySessionListItem> GetSessionPage(
            DateTime? dateFrom,
            DateTime? dateTo,
            string? bossNameContains,
            int skip,
            int take,
            IReadOnlySet<Guid>? excludeSessionIds = null);
        HistorySessionSnapshot? GetSession(Guid sessionId);
        /// <summary>The most recent fights on one boss (exact name) with every player's stats, newest first.</summary>
        IReadOnlyList<BossFightRecord> GetBossFights(string targetName, int max = 300);
        /// <summary>Kills (boss dropped to ≤3% HP) where the user dealt damage, since <paramref name="fromLocal"/>, oldest first.</summary>
        IReadOnlyList<UserKill> GetUserKillsSince(DateTime fromLocal);
    }

    /// <summary>A boss the user's character helped kill (Control Panel "clears this week").</summary>
    public sealed record UserKill(string Boss, string Character, DateTime End);

    /// <summary>One saved fight on a boss, light enough to list many (no hits / skills / buffs).</summary>
    public sealed record BossFightRecord(Guid SessionId, DateTime Start, DateTime End, long TargetHpTotal, long TotalDamage,
        IReadOnlyList<AionDpsMeter.Services.Models.PlayerStats> Players);
}

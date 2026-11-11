namespace AionDpsMeter.Services.Services.FieldBosses
{
    /// <summary>
    /// The field boss list the game sends (about once a second) while the world map is open: for every field
    /// boss spawn on that map, either its exact respawn time or, if it is up, when it spawned and where.
    /// Filled by FieldBossListProcessor; the UI turns it into timers and alerts.
    /// </summary>
    public sealed class FieldBossBoard
    {
        private readonly Lock gate = new();
        private IReadOnlyList<FieldBossEntry> entries = [];

        /// <summary>A list arrived (packet thread). Raised for every list, so handlers should be cheap.</summary>
        public event Action<int, IReadOnlyList<FieldBossEntry>>? Updated;

        public int MapId { get; private set; }
        public DateTime UpdatedAt { get; private set; }

        public IReadOnlyList<FieldBossEntry> Entries { get { lock (gate) return entries; } }

        public void Set(int mapId, IReadOnlyList<FieldBossEntry> list)
        {
            lock (gate)
            {
                MapId = mapId;
                entries = list;
                UpdatedAt = DateTime.Now;
            }
            Updated?.Invoke(mapId, list);
        }
    }

    /// <param name="SpawnId">The game's id for this boss spawn (e.g. 111021 = Immortal Gartua in Altgard).</param>
    /// <param name="IsUp">The boss is alive: <paramref name="TimeUtc"/> is when it spawned.</param>
    /// <param name="TimeUtc">Respawn time (or spawn time when up), exact from the server.</param>
    /// <param name="Position">Map position while up.</param>
    public sealed record FieldBossEntry(int SpawnId, bool IsUp, DateTime TimeUtc, (float X, float Y, float Z)? Position);
}

using System.IO;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Roster;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Roster
{
    /// <summary>
    /// Feeds the Control Panel roster: game events and logins in, roster.json out (2 s after a change).
    /// Runs from startup and never opens anything — the panel is opened by the user.
    /// </summary>
    public sealed class RosterService
    {
        private static readonly string SavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "roster.json");
        private readonly Lock gate = new();
        private readonly ICombatHistoryStore history;
        private readonly ILogger<RosterService> logger;
        private readonly System.Threading.Timer saveTimer;
        private (DateTime At, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Value)? clears;

        private volatile RosterData snapshot;

        public RosterBook Book { get; }
        /// <summary>A copy made under the lock after every change — the panel renders from this, never from Book.Data
        /// (the packet thread adds keys to those dictionaries while the page would be enumerating them).</summary>
        public RosterData Snapshot => snapshot;
        public event Action? Changed;

        public RosterService(RosterEvents events, EntityTracker entities, ICombatHistoryStore history, ILogger<RosterService> logger)
        {
            this.history = history;
            this.logger = logger;
            saveTimer = new System.Threading.Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
            Book = new RosterBook(RosterStore.Load(SavePath), () => DateTime.UtcNow);
            Book.RollOver();
            snapshot = Book.Snapshot();
            Book.Changed += () => { snapshot = Book.Snapshot(); saveTimer.Change(2000, Timeout.Infinite); Changed?.Invoke(); };

            events.Gathered += g => Locked(() => Book.OnGathered(g.ItemId, g.Count + g.Bonus));
            events.MorphFinished += r => Locked(() => Book.OnCrafted(r));
            events.Kina += k => Locked(() => Book.OnKina(k));
            events.Energy += e => Locked(() => Book.OnEnergy(e.Basic, e.Charged));
            entities.UserInfoReceived += id =>
            {
                var p = entities.GetPlayerEntity(id);
                if (string.IsNullOrWhiteSpace(p?.Name)) return;
                Locked(() => Book.SetCharacter(p.Name, p.CharacterClass?.Name ?? ""));   // every login refreshes last-seen
            };
            System.Windows.Application.Current.Exit += (_, _) => Save();
        }

        public void Tick(string? character, string taskId, int delta) => Locked(() => Book.Tick(character, taskId, delta));

        /// <summary>Called by the open panel every second: applies a daily / weekly reset even with no game events.</summary>
        public void RollOverIfDue() => Locked(() =>
        {
            if (!Book.RollOver()) return;
            snapshot = Book.Snapshot();
            saveTimer.Change(2000, Timeout.Infinite);
            Changed?.Invoke();
        });

        public void SaveTaskDefs(List<TaskDef> defs) => Locked(() =>
        {
            Book.Data.TaskDefs = defs;
            Book.RollOver();
            snapshot = Book.Snapshot();
            Changed?.Invoke();
            saveTimer.Change(500, Timeout.Infinite);
        });

        /// <summary>character → boss → kills this week (cached 60 s).</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ClearsThisWeek()
        {
            if (clears is { } c && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(60)) return c.Value;
            var since = RosterClock.WeekStart(DateTime.UtcNow).ToLocalTime();
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> value;
            try
            {
                value = history.GetUserKillsSince(since)
                    .GroupBy(k => k.Character, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.GroupBy(k => k.Boss).ToDictionary(b => b.Key, b => b.Count()),
                                  StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Control Panel clears unavailable: {Error}", ex.Message);
                value = new Dictionary<string, IReadOnlyDictionary<string, int>>();
            }
            clears = (DateTime.UtcNow, value);
            return value;
        }

        private void Locked(Action a) { lock (gate) a(); }

        private void Save()
        {
            try { lock (gate) RosterStore.Save(SavePath, Book.Data); }
            catch (Exception ex) { logger.LogWarning("roster.json save failed: {Error}", ex.Message); }
        }
    }
}

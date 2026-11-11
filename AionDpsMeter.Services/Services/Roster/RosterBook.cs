namespace AionDpsMeter.Services.Services.Roster
{
    /// <summary>
    /// The week per character. Events arrive from the packet thread via the UI service (which locks around calls);
    /// before the first login of a session they are buffered and credited when the name arrives.
    /// </summary>
    public sealed class RosterBook(RosterData data, Func<DateTime> utcNow)
    {
        private readonly List<Action<CharacterWeek>> pending = new();
        public RosterData Data { get; } = data;
        public string? Current { get; private set; }
        /// <summary>Game events waiting for the first login of this session (the panel says so).</summary>
        public int PendingCount => pending.Count;
        public event Action? Changed;

        public void SetCharacter(string name, string className)
        {
            Current = name;
            var c = Get(name);
            c.LastSeenUtc = utcNow();
            if (!string.IsNullOrEmpty(className)) c.ClassName = className;
            RollOver();
            foreach (var a in pending) a(c);
            pending.Clear();
            Changed?.Invoke();
        }

        public void OnGathered(int item, int amount) => Apply(c => c.Gathered[item] = c.Gathered.GetValueOrDefault(item) + amount);
        public void OnCrafted(int recipe) => Apply(c => c.Crafts[recipe] = c.Crafts.GetValueOrDefault(recipe) + 1);
        public void OnKina(long amount) => Apply(c => { c.Kina = amount; c.KinaAtWeekStart ??= amount; });
        public void OnEnergy(int basic, int charged) => Apply(c => { c.EnergyBasic = basic; c.EnergyCharged = charged; c.EnergyAtUtc = utcNow(); });

        /// <summary>+1 / −1 on a hand-ticked task; character null = a server task.</summary>
        public void Tick(string? character, string taskId, int delta)
        {
            var def = Data.TaskDefs.FirstOrDefault(t => t.Id == taskId);
            if (def is null) return;
            RollOver();   // a reset may have passed with no game events since
            var tasks = character is null ? Data.ServerTasks : Get(character).Tasks;
            tasks[taskId] = Math.Clamp(tasks.GetValueOrDefault(taskId) + delta, 0, def.Max);
            Changed?.Invoke();
        }

        /// <summary>Weekly: each character's week moves to LastWeek. Daily tasks (server or per character) clear at the
        /// daily reset, weekly server tasks at the weekly one. Returns true if anything reset.</summary>
        public bool RollOver()
        {
            bool rolled = false;
            var now = utcNow();
            var week = RosterClock.WeekStart(now);
            var day = RosterClock.DayStart(now);
            foreach (var c in Data.Characters.Values)
            {
                if (c.WeekStartUtc >= week) continue;
                if (c.WeekStartUtc != default)
                    c.LastWeek = new CharacterWeek
                    {
                        WeekStartUtc = c.WeekStartUtc, Gathered = c.Gathered, Crafts = c.Crafts, Tasks = c.Tasks,
                        Kina = c.Kina, KinaAtWeekStart = c.KinaAtWeekStart,
                    };
                c.Gathered = new(); c.Crafts = new(); c.Tasks = new();
                c.KinaAtWeekStart = c.Kina;
                c.WeekStartUtc = week;
                rolled = true;
            }
            if (Data.ServerDayStartUtc < day)
            {
                foreach (var t in Data.TaskDefs.Where(t => !t.Weekly))
                {
                    if (t.Server) Data.ServerTasks.Remove(t.Id);
                    else foreach (var c in Data.Characters.Values) c.Tasks.Remove(t.Id);
                }
                Data.ServerDayStartUtc = day;
                rolled = true;
            }
            if (Data.ServerWeekStartUtc < week)
            {
                foreach (var t in Data.TaskDefs.Where(t => t.Server && t.Weekly)) Data.ServerTasks.Remove(t.Id);
                Data.ServerWeekStartUtc = week;
                rolled = true;
            }
            return rolled;
        }

        /// <summary>A deep copy for readers on another thread (the panel renders from this, never from Data).</summary>
        public RosterData Snapshot() => RosterStore.Clone(Data);

        private void Apply(Action<CharacterWeek> change)
        {
            if (Current is null) { pending.Add(change); return; }
            RollOver();
            change(Get(Current));
            Changed?.Invoke();
        }

        private CharacterWeek Get(string name)
        {
            if (!Data.Characters.TryGetValue(name, out var c))
                Data.Characters[name] = c = new CharacterWeek { WeekStartUtc = RosterClock.WeekStart(utcNow()) };
            return c;
        }
    }
}

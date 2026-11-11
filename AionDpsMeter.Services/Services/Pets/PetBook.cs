namespace AionDpsMeter.Services.Services.Pets
{
    /// <summary>
    /// Account-wide pet progress. Soul drops come from inventory updates (see PetParsers); not thread-safe —
    /// the UI's PetTracker calls it under a lock.
    /// </summary>
    public sealed class PetBook(PetData data, PetCatalog catalog)
    {
        /// <summary>More than this many souls added in one entry is not a drop (e.g. a stack listed at login).</summary>
        private const int MaxPerDrop = 5;
        private readonly Dictionary<int, uint> held = new();

        public PetData Data => data;
        public PetCatalog Catalog { get; set; } = catalog;
        public int IgnoredBigStacks { get; private set; }
        public event Action? Changed;

        public PetProgress Get(string pet)
        {
            if (!data.Pets.TryGetValue(pet, out var p)) data.Pets[pet] = p = new PetProgress();
            return p;
        }

        public void Set(string pet, int level, int souls)
        {
            var p = Get(pet);
            p.Level = Math.Clamp(level, 0, PetLevels.Max);
            p.Souls = p.Level >= PetLevels.Max ? 0 : Math.Clamp(souls, 0, PetLevels.NeedFor(p.Level) - 1);
            p.Set = true;
            Touch();
        }

        public void MarkAllMaxed()
        {
            foreach (var pet in Catalog.Pets)
            {
                var p = Get(pet.Name);
                if (p.Set) continue;
                p.Level = PetLevels.Max; p.Souls = 0; p.Set = true;
            }
            Touch();
        }

        public List<PetLevelUp> OnInventory(ReadOnlySpan<byte> msg)
        {
            var ups = new List<PetLevelUp>();
            bool changed = false;
            foreach (var e in PetParsers.FindItems(msg, Catalog.IsSoul))
            {
                uint before = held.GetValueOrDefault(e.ItemId);
                held[e.ItemId] = e.Flag == PetParsers.Removed ? 0 : e.Count;
                // A stack already in the bag only adds its increase; a changed stack we never saw
                // (meter started while holding some) is one drop, not the whole stack.
                long added = e.Flag switch
                {
                    PetParsers.Added => (long)e.Count - before,
                    PetParsers.Changed => before > 0 ? (long)e.Count - before : Math.Min(e.Count, 1u),
                    _ => 0,
                };
                if (added <= 0) continue;
                if (added > MaxPerDrop) { IgnoredBigStacks++; continue; }
                if (Catalog.PetForSoul(e.ItemId) is not { } pet) continue;

                var p = Get(pet.Name);
                if (p.Level >= PetLevels.Max) continue;
                p.Souls += (int)added;
                changed = true;
                while (p.Level < PetLevels.Max && p.Souls >= PetLevels.NeedFor(p.Level))
                {
                    p.Souls -= PetLevels.NeedFor(p.Level);
                    p.Level++;
                    // Pop-ups only for pets the user has set (else the level is a guess) and only Lv2 / Lv3.
                    if (p.Set && p.Level >= 2) ups.Add(new PetLevelUp(pet.Name, p.Level));
                }
                if (p.Level >= PetLevels.Max) p.Souls = 0;
            }
            if (changed) Touch();
            return ups;
        }

        /// <summary>Meter line for the monster being fought: the set, unfinished pet with fewest souls left.</summary>
        public string LineFor(string monster)
        {
            var needed = Catalog.PetsForMonster(monster)
                .Select(p => (Pet: p, Prog: data.Pets.GetValueOrDefault(p.Name)))
                .Where(x => x.Prog is { Set: true, Level: < PetLevels.Max })
                .OrderBy(x => PetLevels.Left(x.Prog!.Level, x.Prog.Souls))
                .ToList();
            if (needed.Count == 0) return "";
            var (pet, prog) = needed[0];
            string level = prog!.Level == 0 ? "new" : $"Lv{prog.Level}";
            string more = needed.Count > 1 ? $" +{needed.Count - 1}" : "";
            return $"🐾 {pet.Name}  {level} {prog.Souls}/{PetLevels.NeedFor(prog.Level)} · {PetLevels.Left(prog.Level, prog.Souls)} left{more}";
        }

        private void Touch()
        {
            data.UpdatedAt = DateTime.UtcNow;
            Changed?.Invoke();
        }
    }
}

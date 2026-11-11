namespace AionDpsMeter.Services.Services.Roster
{
    public sealed record TaskDef(string Id, string Name, int Max, bool Weekly, bool Server, bool Hidden = false)
    {
        /// <summary>Server = shared by every character on the server (aion2data.com/alts, game files 2026-10-21);
        /// the per-character list partly comes from guides, so every entry can be renamed / hidden in the panel.</summary>
        public static List<TaskDef> Defaults() =>
        [
            new("duty", "Duty missions", 5, Weekly: false, Server: true),
            new("dailydungeon", "Daily Dungeon entries", 14, true, true),
            new("shugokey", "Shugo Festival reward key", 1, true, true),
            new("invasionkey", "Dimensional Invasion reward key", 1, true, true),
            new("cmdshop", "Command shop scrolls", 1, true, true),
            new("odyleshop", "Odyle shops", 1, true, true),
            new("abyss-lower", "Abyss hours · Lower", 7, true, false),
            new("abyss-middle", "Abyss hours · Middle", 7, true, false),
            new("abyss-upper", "Abyss hours · Upper", 7, true, false),
            new("ascension", "Ascension Trial", 3, true, false),
            new("nightmare", "Nightmare", 1, true, false),
            new("subjugation", "Subjugation", 3, true, false),
            new("sanctuary", "Sanctuary", 4, true, false),
            new("expedition", "Expedition rewards", 7, true, false),
            new("battlefield", "Battlefield wins", 3, true, false),
            new("conquest", "Conquest / Transcendence rewards", 1, true, false),
            new("daevapass", "Daeva Pass weekly EXP", 1, true, false),
        ];
    }

    public sealed class CharacterWeek
    {
        public DateTime LastSeenUtc { get; set; }
        public string ClassName { get; set; } = "";
        public long? Kina { get; set; }
        public long? KinaAtWeekStart { get; set; }
        public int? EnergyBasic { get; set; }
        public int? EnergyCharged { get; set; }
        public DateTime? EnergyAtUtc { get; set; }
        public DateTime WeekStartUtc { get; set; }
        public Dictionary<int, int> Gathered { get; set; } = new();
        public Dictionary<int, int> Crafts { get; set; } = new();
        public Dictionary<string, int> Tasks { get; set; } = new();
        public CharacterWeek? LastWeek { get; set; }
    }

    public sealed class RosterData
    {
        public Dictionary<string, CharacterWeek> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ServerTasks { get; set; } = new();
        public DateTime ServerDayStartUtc { get; set; }
        public DateTime ServerWeekStartUtc { get; set; }
        public List<TaskDef> TaskDefs { get; set; } = TaskDef.Defaults();
    }
}

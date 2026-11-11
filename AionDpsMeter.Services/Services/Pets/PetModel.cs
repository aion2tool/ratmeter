namespace AionDpsMeter.Services.Services.Pets
{
    /// <summary>Souls per pet level: 5 to summon (Lv1), +25 to Lv2, +75 to Lv3 (max) = 105.</summary>
    public static class PetLevels
    {
        public const int Max = 3;
        public static readonly int[] Need = [5, 25, 75];
        public const int TotalToMax = 105;

        public static int NeedFor(int level) => level >= Max ? 0 : Need[Math.Max(0, level)];

        public static int Left(int level, int souls)
        {
            if (level >= Max) return 0;
            int done = Need.Take(Math.Max(0, level)).Sum() + souls;
            return Math.Max(0, TotalToMax - done);
        }
    }

    public sealed class PetProgress
    {
        public int Level { get; set; }
        /// <summary>Souls toward the next level (what the game's pet window shows, e.g. 54 of 75).</summary>
        public int Souls { get; set; }
        /// <summary>True once the user has set this pet's starting point (or Mark all maxed).</summary>
        public bool Set { get; set; }
    }

    /// <summary>Account-wide: pets are shared by every character of the account.</summary>
    public sealed class PetData
    {
        public Dictionary<string, PetProgress> Pets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime UpdatedAt { get; set; }
    }

    public readonly record struct PetLevelUp(string Pet, int Level);
}

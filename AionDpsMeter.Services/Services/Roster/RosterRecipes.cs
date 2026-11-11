namespace AionDpsMeter.Services.Services.Roster
{
    /// <summary>Substance Morph recipes the panel names (from the user's crafting recording, 2026-10-22).</summary>
    public static class RosterRecipes
    {
        public static readonly IReadOnlySet<int> OdyleEnergy = new HashSet<int> { 31070001, 31070002 };

        public static int EnergyCrafts(CharacterWeek w) => w.Crafts.Where(kv => OdyleEnergy.Contains(kv.Key)).Sum(kv => kv.Value);
        public static int OtherCrafts(CharacterWeek w) => w.Crafts.Where(kv => !OdyleEnergy.Contains(kv.Key)).Sum(kv => kv.Value);
    }
}

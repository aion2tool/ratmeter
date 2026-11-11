namespace AionDpsMeter.Core.Models
{
    public class Player : Entity
    {
        public string? Icon { get; set; }
        public int CharacterLevel { get; set; }
        public int CombatPower { get; set; }
        /// <summary>Equipped item level ("gear score"); only the party packet carries it, 0 = unknown.</summary>
        public int GearScore { get; set; }
        public int ServerId { get; set; }
        public string ServerName { get; set; } = "";
        public bool IsUser { get; set; }
        public bool IsIdentified { get; set; }
        public CharacterClass? CharacterClass { get; set; }
        public int? GlobalId { get; set; }
    }
}
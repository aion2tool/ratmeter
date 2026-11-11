namespace AionDpsMeter.UI.ViewModels
{
    public class PlayerRenderState
    {
        public long PlayerId { get; set; }
        public bool IsUser { get; set; }
        public string ClassId { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public int ServerId { get; set; }

        public string PlayerNameDisplay { get; set; } = string.Empty;
        /// <summary>Real (unmasked) name, for gear lookup when the tracker no longer holds the entity.</summary>
        public string PlayerName { get; set; } = string.Empty;
        public string DeathsDisplay { get; set; } = string.Empty;

        public long TotalDamage { get; set; }
        public string TotalDamageFormatted { get; set; } = string.Empty;
        public string DpsFormatted { get; set; } = string.Empty;
        public double Dps { get; set; }
        public double DamagePercentage { get; set; }
        public string CombatPower { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string? ClassIcon { get; set; }
        public double CriticalRate { get; set; }

        public double VisualAbsolutePercentage { get; set; }
        public double VisualRelativePercentage { get; set; }
        public double EffectivePercentage { get; set; }
    }
}

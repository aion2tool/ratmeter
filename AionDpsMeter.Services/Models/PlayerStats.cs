namespace AionDpsMeter.Services.Models
{
    public sealed class PlayerStats
    {
        public long PlayerId { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public bool IsUser { get; init; }
        public bool IsIdentified { get; init;  }
        public string? PlayerIcon { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public int ClassId { get; set; }
        public string? ClassIcon { get; set; }
        public int CombatPower { get; set; }
        public int GearScore { get; set; }
        public string ServerName { get; set; } = string.Empty;
        /// <summary>Numeric game server id (0 = unknown).</summary>
        public int ServerId { get; set; }
        public int PlayerDeaths { get; set; }
        public long TotalDamage { get; set; }
        public int HitCount { get; set; }
        public int CriticalHits { get; set; }
        public int BackAttacks { get; set; }
        public int FrontAttacks { get; set; }
        public int PerfectHits { get; set; }
        public int DoubleDamageHits { get; set; }
        public int ParryHits { get; set; }
        /// <summary>Damage over the whole fight (what the meter ranks by).</summary>
        public double DamagePerSecond { get; set; }
        /// <summary>Damage over this player's own first-to-last hit (higher if they joined late or died early).</summary>
        public double ActiveDamagePerSecond { get; set; }
        public double DamagePercentage { get; set; }
        public DateTime FirstHit { get; set; }
        public DateTime LastHit { get; set; }

        public double CriticalRate => HitCount > 0 ? (double)CriticalHits / HitCount * 100 : 0;
        public double BackAttackRate => HitCount > 0 ? (double)BackAttacks / HitCount * 100 : 0;
        public double FrontAttackRate => HitCount > 0 ? (double)FrontAttacks / HitCount * 100 : 0;
        public double PerfectRate => HitCount > 0 ? (double)PerfectHits / HitCount * 100 : 0;
        public double DoubleDamageRate => HitCount > 0 ? (double)DoubleDamageHits / HitCount * 100 : 0;
        public double ParryRate => HitCount > 0 ? (double)ParryHits / HitCount * 100 : 0;
        public TimeSpan CombatDuration => LastHit > FirstHit ? LastHit - FirstHit : TimeSpan.Zero;
    }
}

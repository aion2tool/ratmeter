using AionDpsMeter.Core.Data;
using AionDpsMeter.Core.GameData.Services;

namespace AionDpsMeter.Core.Models
{
    public class Mob : Entity
    {
        private const int BossHpThreshold = 100_000_000;

        public int MobCode { get; set; }
        public long HpTotal { get; set; }
        public long HpCurrent { get; set; }
        /// <summary>Current HP has been received from the game (until then HpCurrent is just 0, not "dead").</summary>
        public bool HpKnown { get; set; }
        /// <summary>The game reported this mob's death.</summary>
        public bool DeathSeen { get; set; }
        public bool IsDead => DeathSeen || (HpKnown && HpTotal > 0 && HpCurrent <= 0);
        /// <summary>Total HP this mob has gained back (heals / regen) while alive, for the fight log.</summary>
        public long HpRegained { get; set; }
        //public new string Name => GetMobName();
        //public bool IsBoss => CanBeBoss();

        public new string Name => GameDataProvider.Instance.GetMobName(MobCode);
        public bool IsBoss => GameDataProvider.Instance.IsBoss(MobCode);

        public bool IsDummy => GameDataProvider.Instance.IsDummy(MobCode);

        private string GetMobName()
        {
            if (GameDataProvider.Instance.IsKnownMob(MobCode)) return GameDataProvider.Instance.GetMobName(MobCode);
            if (HpTotal >= BossHpThreshold) return $"Unknown boss {MobCode}";
            return $"Unknown {MobCode}";
        }

        private bool CanBeBoss()
        {
            if (GameDataProvider.Instance.IsKnownMob(MobCode)) return GameDataProvider.Instance.IsBoss(MobCode);
            if (HpTotal >= BossHpThreshold) return true;
            return false;
        }
    }
}

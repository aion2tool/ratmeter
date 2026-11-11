namespace AionDpsMeter.Services.PacketProcessing.Routing;

public static class PacketOpcodes
{
    public const ushort ServerTime = 0x3603;          // 03 36
    public const ushort Damage = 0x3804;              // 04 38
    public const ushort DotDamage = 0x3805;           // 05 38
    public const ushort CompressedStream = 0xFFFF;    // FF FF
    public const ushort RemainHp = 0x8D00;            // 00 8D
    public const ushort MobSummon = 0x3641;           // 41 36
    public const ushort BuffEffectA = 0x382A;         // 2A 38
    public const ushort BuffEffectB = 0x382B;         // 2B 38
    public const ushort PlayerInfo = 0x3633;          // 33 36
    public const ushort OtherPlayersInfo = 0x3645;    // 45 36
    public const ushort GlobalSessIdLinking = 0x3620; // 20 36
    public const ushort PlayerStats = 0x3649;         // 49 36
    public const ushort PartyInfo = 0x9702;           // 02 97
    public const ushort EntityDeath = 0x8D04;         // 04 8D
    public const ushort SkillCd = 0x3847;             // 47 38
    public const ushort ZoneLoaded = 0x4005;          // 05 40  end of a loading screen
    public const ushort FieldBossList = 0x9101;       // 01 91  field boss respawn list (while the map is open)
    public const ushort ItemGathered = 0x8D33;        // 33 8D  item gathered (Control Panel)
    public const ushort MorphFinished = 0x8D8B;       // 8B 8D  Substance Morph finished
    public const ushort InventoryUpdate = 0x561B;     // 1B 56  inventory / currency slots
    public const ushort OdyleEnergy = 0x610C;         // 0C 61  Odyle energy changed
}
using AionDpsMeter.Services.Services.Roster;
using Xunit;

// Hex lines are the user's real recordings of 2026-10-22 (Logs/messages-20261022-*.txt).
public class RosterParsersTests
{
    static byte[] H(string hex) => Convert.FromHexString(hex);

    static readonly string[] Gathers =
    [
        "1F338D003AD6FE025D2B652401000000000000000000000000000000",
        "1F338D003AD6FE025D2B652401000000010000000000000000000000",
        "1F338D003AD6FE025D2B652401000000000000000000000000000000",
        "1F338D013ABCFA02602B652401000000000000000000000000000000",
        "1F338D003ABCFA025D2B652401000000000000000000000000000000",
        "1F338D003ABCFA025D2B652401000000010000000000000000000000",
        "1F338D013A82ED025E2B652401000000000000000000000000000000",
    ];

    [Fact]
    public void Gathers_add_up_to_the_users_real_haul()
    {
        var total = new Dictionary<int, int>();
        var rare = new List<int>();
        foreach (var h in Gathers)
        {
            Assert.True(RosterParsers.TryParseGather(H(h), out var g));
            total[g.ItemId] = total.GetValueOrDefault(g.ItemId) + g.Count + g.Bonus;
            if (g.Rare) rare.Add(g.ItemId);
        }
        Assert.Equal(7, total[610610013]);   // Odyle
        Assert.Equal(1, total[610610014]);   // Fine Odyle
        Assert.Equal(1, total[610610016]);   // Radiant Odyle
        Assert.Equal([610610016, 610610014], rare);
    }

    [Theory]
    [InlineData("2F8B8D0000013217DA0150053217DA0101000000010000000000000000000000000000000000000001000000", 31070002)]
    [InlineData("2F8B8D0000013117DA0150053117DA0101000000010000000000000000000000000000000100000000000000", 31070001)]
    public void Morph_finished_gives_the_recipe(string hex, int recipe)
    {
        Assert.True(RosterParsers.TryParseMorph(H(hex), out var r));
        Assert.Equal(recipe, r);
    }

    [Fact]
    public void Energy_message_from_using_an_odyle_energy_item()
    {
        Assert.True(RosterParsers.TryParseEnergy(H("130C61010C018793035A550128000000"), out var e));
        Assert.Equal(new EnergyInfo(90, 85, 40), e);
    }

    [Theory]
    [InlineData("150C61010C01879303AC02DC0B0128000000", 300, 1500)]   // 2-byte varints
    [InlineData("130C61010C01879303003F0128000000", 0, 63)]
    public void Energy_values_of_any_size(string hex, int basic, int charged)
    {
        Assert.True(RosterParsers.TryParseEnergy(H(hex), out var e));
        Assert.Equal(basic, e.Basic);
        Assert.Equal(charged, e.Charged);
    }

    const string AfterCraft1 = "9A021B5607010062881200007B00005F2B652404000000000000000146000000000000000000000000000037000200C122050000EC0000525896250000000000000000013B0000000000000000000000000037000100FDD90500000301005D2B65241E00000000000000010B000000000000000000000000003700010048930300004D010021337037726A00000000000001010000000000000000000000000037000100CF67130000220000525896250400000000000000023900000020000000000000000000003700020038730A00004301005158962500000000000000000243000000000000000000000000003700010049B30E00003401003FDA051E1F000000000000000131000000008099A67DA20100002C0000";
    const string AfterCraft2 = "9A021B56070100A8C51200006C00003F3370377132400000000000010000000000000000000000000000003700010062881200007B00005F2B65240300000000000000014600000000000000000000000000370001004C600E00008F00005E2B65240B0000000000000001410000000000000000000000000037000100FDD90500000301005D2B65240500000000000000010B000000000000000000000000003700020048930300004D01002133703700000000000000000101000000200000000000000000000037000200CF671300002200005258962500000000000000000239000000000000000000000000003700010049B30E00003401003FDA051E20000000000000000131000000008099A67DA20100002C0000";

    [Theory]
    [InlineData(AfterCraft1, 610610015, 4)]
    [InlineData(AfterCraft2, 610610015, 3)]
    [InlineData(AfterCraft1, 610610013, 30)]
    [InlineData(AfterCraft2, 610610013, 5)]
    [InlineData(AfterCraft2, 930100031, 4207217)]   // Kina, confirmed in game
    public void Inventory_update_counts(string hex, int item, long expected)
    {
        Assert.True(RosterParsers.TryFindItemCount(H(hex), item, out var n));
        Assert.Equal(expected, n);
    }

    [Fact]
    public void Item_not_in_the_update_is_reported_missing() =>
        Assert.False(RosterParsers.TryFindItemCount(H(AfterCraft1), 930100031, out _));

    [Theory]
    [InlineData("1F338D003AD6FE02")]                                            // truncated gather
    [InlineData("1F348D003AD6FE025D2B652401000000000000000000000000000000")]     // wrong opcode
    [InlineData("")]
    public void Bad_input_is_rejected_not_thrown(string hex)
    {
        Assert.False(RosterParsers.TryParseGather(H(hex), out _));
        Assert.False(RosterParsers.TryParseMorph(H(hex), out _));
        Assert.False(RosterParsers.TryParseEnergy(H(hex), out _));
    }
}

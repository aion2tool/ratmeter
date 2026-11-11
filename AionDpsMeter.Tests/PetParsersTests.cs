using AionDpsMeter.Services.Services.Pets;
using Xunit;

// Hex is the user's recording of 2026-11-01 12:11-12:14 (farming Mutated Aberrant Odyle Spider souls).
public class PetParsersTests
{
    const int Spider = 534660020, Agrint = 534660199, WitheredAgrint = 534660026, Papis = 534660067;
    static readonly HashSet<int> Souls = [Spider, Agrint, WitheredAgrint, Papis];
    static byte[] H(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void Soul_arrives_as_added_one()
    {
        var e = PetParsers.FindItems(H("2F1B560100005A3D090000810000B443DE1F0100000000000000040000000000000000000000000000050000"), Souls.Contains);
        Assert.Equal([new ItemEntry(Spider, PetParsers.Added, 1)], e);
    }

    [Fact]
    public void Absorbed_soul_is_removed_zero()
    {
        var e = PetParsers.FindItems(H("2F1B560102005A3D090000810000B443DE1F0000000000000000040000000000000000000000000000002600"), Souls.Contains);
        Assert.Equal([new ItemEntry(Spider, PetParsers.Removed, 0)], e);
    }

    [Fact]
    public void Kina_update_has_no_souls()
    {
        Assert.Empty(PetParsers.FindItems(H("1B1B5601010000000000000000003F337037713240000000000000"), Souls.Contains));
    }

    [Fact]
    public void Other_message_types_are_ignored()
    {
        Assert.Empty(PetParsers.FindItems(H("1F338D003AD6FE025D2B652401000000000000000000000000000000"), Souls.Contains));
    }

    [Fact]
    public void Whole_recording_adds_the_users_souls()
    {
        var added = new Dictionary<int, int>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", "inventory-20261101.txt")))
            foreach (var e in PetParsers.FindItems(H(line.Trim()), Souls.Contains))
                if (e.Flag == PetParsers.Added) added[e.ItemId] = added.GetValueOrDefault(e.ItemId) + (int)e.Count;
        Assert.Equal(35, added[Spider]);
        Assert.Equal(6, added[Agrint]);
        Assert.Equal(4, added[WitheredAgrint]);
        Assert.Equal(4, added[Papis]);
    }
}

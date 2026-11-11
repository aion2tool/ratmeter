using AionDpsMeter.Services.Services.Pets;
using Xunit;

public class PetBookTests
{
    const string Spider = "Mutated Aberrant Odyle Spider";
    const string Add1 = "2F1B560100005A3D090000810000B443DE1F0100000000000000040000000000000000000000000000050000";
    const string Remove = "2F1B560102005A3D090000810000B443DE1F0000000000000000040000000000000000000000000000002600";
    static byte[] H(string hex) => Convert.FromHexString(hex);

    // Same entry with a different flag (byte 4 = body[1]) and count (u32 after the id at byte 14).
    static byte[] Entry(byte flag, uint count)
    {
        var b = H(Add1);
        b[4] = flag;
        BitConverter.GetBytes(count).CopyTo(b, 18);
        return b;
    }

    static PetCatalog Cat() => new(new PetCatalogData
    {
        Pets =
        [
            new PetInfo(Spider, "Fera", "u", [534660020], [new PetSource("Marsh Odyle Spider", "15", 100, ["Verteron · Contaminated Marsh"])]),
            new PetInfo("Agrint", "Other", "u", [534660199], [new PetSource("Agrint", "20", 35.5, [])]),
        ],
    });

    static PetBook Book(int level, int souls)
    {
        var b = new PetBook(new PetData(), Cat());
        b.Set(Spider, level, souls);
        return b;
    }

    [Fact]
    public void A_drop_adds_one_soul()
    {
        var b = Book(2, 54);
        b.OnInventory(H(Add1));
        b.OnInventory(H(Remove));
        Assert.Equal(55, b.Get(Spider).Souls);
    }

    [Fact]
    public void Reaching_75_levels_to_max()
    {
        var b = Book(2, 74);
        var ups = b.OnInventory(H(Add1));
        Assert.Equal([new PetLevelUp(Spider, 3)], ups);
        Assert.Equal(3, b.Get(Spider).Level);
        Assert.Equal(0, b.Get(Spider).Souls);
    }

    [Fact]
    public void Level_1_carry_over_goes_into_level_2()
    {
        var b = Book(1, 24);
        var ups = b.OnInventory(Entry(PetParsers.Added, 2));
        Assert.Equal([new PetLevelUp(Spider, 2)], ups);
        Assert.Equal(1, b.Get(Spider).Souls);
    }

    [Fact]
    public void Maxed_pet_ignores_souls()
    {
        var b = Book(3, 0);
        Assert.Empty(b.OnInventory(H(Add1)));
        Assert.Equal(0, b.Get(Spider).Souls);
    }

    [Fact]
    public void Unset_pet_still_counts_from_zero()
    {
        var b = new PetBook(new PetData(), Cat());
        b.OnInventory(H(Add1));
        Assert.False(b.Get(Spider).Set);
        Assert.Equal(1, b.Get(Spider).Souls);
    }

    [Fact]
    public void Big_added_stack_is_ignored()
    {
        var b = Book(2, 10);
        b.OnInventory(Entry(PetParsers.Added, 40));   // e.g. a held stack listed at login
        Assert.Equal(10, b.Get(Spider).Souls);
        Assert.Equal(1, b.IgnoredBigStacks);
    }

    [Fact]
    public void Changed_entry_counts_only_the_increase()
    {
        var b = Book(2, 10);
        b.OnInventory(Entry(PetParsers.Added, 1));
        b.OnInventory(Entry(PetParsers.Changed, 3));
        Assert.Equal(13, b.Get(Spider).Souls);
    }

    [Fact]
    public void Empty_catalog_counts_nothing()
    {
        var b = new PetBook(new PetData(), PetCatalog.Empty);
        Assert.Empty(b.OnInventory(H(Add1)));
        Assert.Empty(b.Data.Pets);
    }

    [Fact]
    public void Meter_line_for_a_needed_pet()
    {
        Assert.Equal("🐾 Mutated Aberrant Odyle Spider  Lv2 54/75 · 21 left", Book(2, 54).LineFor("Marsh Odyle Spider"));
    }

    [Fact]
    public void No_line_for_maxed_unset_or_unknown()
    {
        Assert.Equal("", Book(3, 0).LineFor("Marsh Odyle Spider"));
        Assert.Equal("", new PetBook(new PetData(), Cat()).LineFor("Marsh Odyle Spider"));
        Assert.Equal("", Book(2, 54).LineFor("Punching Bag"));
    }

    [Fact]
    public void Unset_pet_levelling_raises_no_pop_up()
    {
        var b = new PetBook(new PetData(), Cat());
        for (int i = 0; i < 5; i++) { b.OnInventory(H(Add1)); b.OnInventory(H(Remove)); }
        Assert.Equal(1, b.Get(Spider).Level);                 // still counted
        var ups = new List<PetLevelUp>();
        for (int i = 0; i < 25; i++) { ups.AddRange(b.OnInventory(H(Add1))); b.OnInventory(H(Remove)); }
        Assert.Equal(2, b.Get(Spider).Level);
        Assert.Empty(ups);                                     // its real level is unknown: no "PET LEVEL 2!"
    }

    [Fact]
    public void Reaching_level_1_raises_no_pop_up()
    {
        var b = Book(0, 4);
        Assert.Empty(b.OnInventory(H(Add1)));
        Assert.Equal(1, b.Get(Spider).Level);
    }

    [Fact]
    public void Added_entry_for_a_held_stack_counts_only_the_increase()
    {
        var b = Book(2, 10);
        b.OnInventory(Entry(PetParsers.Added, 1));   // stack of 1 stays in the bag
        b.OnInventory(Entry(PetParsers.Added, 2));   // listed again with 2
        Assert.Equal(12, b.Get(Spider).Souls);
    }

    [Fact]
    public void Changed_entry_without_a_known_stack_counts_one()
    {
        var b = Book(2, 10);
        b.OnInventory(Entry(PetParsers.Changed, 4));  // meter restarted while 3 were held
        Assert.Equal(11, b.Get(Spider).Souls);
    }

    [Fact]
    public void Mark_all_maxed_keeps_pets_already_set()
    {
        var b = Book(2, 54);
        b.MarkAllMaxed();
        Assert.Equal(2, b.Get(Spider).Level);
        Assert.Equal(3, b.Get("Agrint").Level);
        Assert.True(b.Get("Agrint").Set);
    }
}

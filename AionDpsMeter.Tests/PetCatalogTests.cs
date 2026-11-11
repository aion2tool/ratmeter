using AionDpsMeter.Services.Services.Pets;
using Xunit;

public class PetCatalogTests
{
    // Trimmed copies of aion2data.com/raw/monsters.json and map-markers.json (client 2026.10.28).
    const string Monsters = """
    {"clientVersion":"2026.10.28","records":[
     {"name":"Marsh Odyle Spider","level":[15,15],"spawns":[{"map":"Verteron","area":"Contaminated Marsh","points":34}],
      "drops":[{"id":534660020,"item":"Soul: Mutated Aberrant Odyle Spider (Bound)","chance":100},{"id":610530014,"item":"Spiritstone","chance":3.8889}]},
     {"name":"Unstable Ashen Spider","level":[48,48],"spawns":[{"map":"Altgard","area":"Ashen Hollow","points":12}],
      "drops":[{"id":534660020,"item":"Soul: Mutated Aberrant Odyle Spider (Bound)","chance":100}]},
     {"name":"Agrint","level":[20,22],"spawns":[{"map":"Verteron","area":"Tursin Garrison","points":5}],
      "drops":[{"id":534660199,"item":"Soul: Agrint (Bound)","chance":35.5}]},
     {"name":"Punching Bag","level":[1,1],"spawns":[],"drops":[]}
    ]}
    """;
    const string Markers = """
    {"records":[
     {"map":"Verteron","layer":"pets","name":"Mutated Aberrant Odyle Spider","type":"Fera","url":"https://aion2data.com/pets/1025-mutated-aberrant-odyle-spider"},
     {"map":"Verteron","layer":"cubes","name":"Hidden Cube","type":"","url":""}
    ]}
    """;

    static PetCatalog Cat() => new(PetCatalog.Build(Monsters, Markers, new DateTime(2026, 11, 1)));

    [Fact]
    public void Soul_item_maps_to_its_pet()
    {
        var pet = Cat().PetForSoul(534660020)!;
        Assert.Equal("Mutated Aberrant Odyle Spider", pet.Name);
        Assert.Equal("Fera", pet.Family);
        Assert.Equal("https://aion2data.com/pets/1025-mutated-aberrant-odyle-spider", pet.Url);
        Assert.Equal(["Marsh Odyle Spider", "Unstable Ashen Spider"], pet.Sources.Select(s => s.Monster));
        Assert.Equal("15", pet.Sources[0].Level);
        Assert.Equal(100, pet.Sources[0].Chance);
        Assert.Equal(["Verteron · Contaminated Marsh"], pet.Sources[0].Places);
    }

    [Fact]
    public void Monster_lookup_is_case_insensitive()
    {
        Assert.Equal("Mutated Aberrant Odyle Spider", Assert.Single(Cat().PetsForMonster("marsh odyle spider")).Name);
        Assert.Empty(Cat().PetsForMonster("Punching Bag"));
    }

    [Fact]
    public void Pet_without_marker_gets_other_family_and_go_link()
    {
        var pet = Cat().PetForSoul(534660199)!;
        Assert.Equal("Other", pet.Family);
        Assert.Equal("https://aion2data.com/go/Agrint", pet.Url);
        Assert.Equal("20–22", pet.Sources[0].Level);
    }

    [Fact]
    public void Non_souls_are_not_souls()
    {
        Assert.False(Cat().IsSoul(610530014));
        Assert.True(Cat().IsSoul(534660020));
        Assert.False(PetCatalog.Empty.IsSoul(534660020));
    }

    [Theory]
    [InlineData("Soul: Papis (Bound)", "Papis")]
    [InlineData("Soul: Papis", "Papis")]
    [InlineData("Spiritstone", "")]
    public void Pet_name_from_soul_item(string item, string pet) => Assert.Equal(pet, PetCatalog.PetNameFromSoul(item));
}

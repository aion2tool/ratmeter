using AionDpsMeter.Services.Services.Pets;
using Xunit;

public class PetStoreTests
{
    [Theory]
    [InlineData(0, 0, 105)]
    [InlineData(2, 54, 21)]   // the user's spider on 2026-11-01: Lv2 54/75
    [InlineData(3, 0, 0)]
    public void Souls_left_to_max(int level, int souls, int left) => Assert.Equal(left, PetLevels.Left(level, souls));

    [Fact]
    public void Save_then_load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pets-{Guid.NewGuid():N}.json");
        var d = new PetData();
        d.Pets["Mutated Aberrant Odyle Spider"] = new PetProgress { Level = 2, Souls = 54, Set = true };
        PetStore.Save(path, d);
        var back = PetStore.Load(path);
        Assert.Equal(54, back.Pets["mutated aberrant odyle spider"].Souls);   // case-insensitive keys survive
        File.Delete(path);
    }

    [Fact]
    public void Locked_file_is_a_read_failure_not_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pets-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Pets":{"Papis":{"Level":2,"Souls":3,"Set":true}}}""");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var d = PetStore.Load(path, out bool readFailed);
            Assert.True(readFailed);                  // caller must not save over the real file
            Assert.Empty(d.Pets);
            Assert.False(File.Exists(path + ".bad"));
        }
        Assert.Equal(3, PetStore.Load(path, out bool again).Pets["Papis"].Souls);
        Assert.False(again);
        File.Delete(path);
    }

    [Fact]
    public void Corrupt_file_loads_empty_and_is_kept_as_bad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pets-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        Assert.Empty(PetStore.Load(path).Pets);
        Assert.True(File.Exists(path + ".bad"));
        File.Delete(path); File.Delete(path + ".bad");
    }
}

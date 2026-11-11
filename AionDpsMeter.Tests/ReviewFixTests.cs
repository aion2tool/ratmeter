using AionDpsMeter.Core.GameData.Repositories;
using AionDpsMeter.Services.Services.Roster;
using Xunit;

// Fixes from the final review of the Control Panel branch (2026-10-22).
public class ReviewFixTests
{
    DateTime now = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Utc);
    RosterBook New(RosterData? d = null) => new(d ?? new RosterData(), () => now);
    static byte[] H(string hex) => Convert.FromHexString(hex);

    // Important #1: the page renders from a snapshot, never from the dictionaries the packet thread writes.
    [Fact]
    public void Snapshot_is_independent_of_later_changes()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar"); b.OnGathered(610610015, 1);
        var snap = b.Snapshot();
        b.OnGathered(610610013, 5); b.SetCharacter("AltOne", "Cleric");
        Assert.Single(snap.Characters);
        Assert.False(snap.Characters["Hero"].Gathered.ContainsKey(610610013));
        Assert.Equal(1, snap.Characters["Hero"].Gathered[610610015]);
    }

    // Important #2: a tick after a reset lands in the new day/week even with no game events in between.
    [Fact]
    public void Tick_after_the_daily_reset_counts_for_the_new_day()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar");
        for (int i = 0; i < 5; i++) b.Tick(null, "duty", 1);
        now = now.AddDays(1);
        b.Tick(null, "duty", 1);
        Assert.Equal(1, b.Data.ServerTasks["duty"]);
    }

    [Fact]
    public void RollOver_reports_whether_anything_reset()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar");
        Assert.False(b.RollOver());
        now = now.AddDays(1);
        Assert.True(b.RollOver());
        Assert.False(b.RollOver());
    }

    // Important #3: a per-character daily task (added in Edit tasks) resets every day.
    [Fact]
    public void Per_character_daily_tasks_reset_daily()
    {
        var d = new RosterData();
        d.TaskDefs.Add(new TaskDef("dailychar", "Daily thing", 3, Weekly: false, Server: false));
        var b = New(d);
        b.SetCharacter("Hero", "Templar");
        b.Tick("Hero", "dailychar", 1); b.Tick("Hero", "dailychar", 1); b.Tick("Hero", "ascension", 1);
        now = now.AddDays(1);
        b.RollOver();
        Assert.Equal(0, b.Data.Characters["Hero"].Tasks.GetValueOrDefault("dailychar"));
        Assert.Equal(1, b.Data.Characters["Hero"].Tasks["ascension"]);   // weekly ones stay
    }

    // Important #4: "Energy crafted" counts only the Odyle Energy recipes.
    [Fact]
    public void Energy_crafts_count_only_odyle_energy_recipes()
    {
        var w = new CharacterWeek { Crafts = new() { [31070001] = 2, [31070002] = 1, [31099999] = 4 } };
        Assert.Equal(3, RosterRecipes.EnergyCrafts(w));
        Assert.Equal(4, RosterRecipes.OtherCrafts(w));
    }

    // Important #4 / Review Focus 1: layouts that don't look like the recorded ones are rejected.
    [Theory]
    [InlineData("2F8B8D0100013217DA0150053217DA0101000000")]                    // morph without the 00 00 01 prefix
    public void Morph_with_an_unknown_prefix_is_rejected(string hex) => Assert.False(RosterParsers.TryParseMorph(H(hex), out _));

    [Theory]
    [InlineData("1F338D003BD6FE025D2B652401000000000000000000000000000000")]  // second byte not 3A
    [InlineData("1F338D093AD6FE025D2B652401000000000000000000000000000000")]  // flag 9
    [InlineData("1F338D003AD6FE025D2B6524FFFFFF7F010000000000000000000000")]  // count int.Max + bonus 1 (overflow)
    public void Gather_with_an_unknown_layout_is_rejected(string hex) => Assert.False(RosterParsers.TryParseGather(H(hex), out _));

    [Fact]
    public void Energy_with_an_unknown_prefix_is_rejected() =>
        Assert.False(RosterParsers.TryParseEnergy(H("130C61020C018793035A550128000000"), out _));

    // Important #5: training dummies are not boss clears.
    [Fact]
    public void Training_dummies_are_known_by_name()
    {
        var repo = new MobRepository();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AionDpsMeter.Core", "GameData", "Assets", "mobs.json"));
        repo.Load(path);
        Assert.True(repo.IsDummyName("Training Scarecrow"));
        Assert.True(repo.IsDummyName("Melee Training Scarecrow"));
        Assert.False(repo.IsDummyName("Divine Auldor"));
    }

    // Minor #6 (re-graded): the panel can say how many events wait for the next login.
    [Fact]
    public void Pending_count_shows_events_waiting_for_login()
    {
        var b = New();
        b.OnGathered(610610015, 1); b.OnKina(5);
        Assert.Equal(2, b.PendingCount);
        b.SetCharacter("Hero", "Templar");
        Assert.Equal(0, b.PendingCount);
    }
}

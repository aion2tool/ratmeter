using AionDpsMeter.Services.Services.Roster;
using Xunit;

public class RosterBookTests
{
    DateTime now = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Utc);   // Thursday, week of Wed 21st
    RosterBook New(RosterData? d = null) => new(d ?? new RosterData(), () => now);

    [Fact]
    public void Events_go_to_the_current_character()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar");
        b.OnGathered(610610015, 1); b.OnGathered(610610013, 2); b.OnCrafted(31070001); b.OnKina(4207217); b.OnEnergy(90, 85);
        var c = b.Data.Characters["Hero"];
        Assert.Equal(1, c.Gathered[610610015]); Assert.Equal(2, c.Gathered[610610013]);
        Assert.Equal(1, c.Crafts[31070001]); Assert.Equal(4207217, c.Kina); Assert.Equal(4207217, c.KinaAtWeekStart);
        Assert.Equal((90, 85), (c.EnergyBasic, c.EnergyCharged));
        Assert.Equal(now, c.LastSeenUtc);
    }

    [Fact]
    public void Events_before_login_are_credited_when_the_name_arrives()
    {
        var b = New();
        b.OnGathered(610610015, 1);
        Assert.Empty(b.Data.Characters);
        b.SetCharacter("AltOne", "Cleric");
        Assert.Equal(1, b.Data.Characters["AltOne"].Gathered[610610015]);
    }

    [Fact]
    public void Switching_characters_keeps_each_ones_counts()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar"); b.OnGathered(610610015, 1);
        b.SetCharacter("AltOne", "Cleric"); b.OnGathered(610610015, 2);
        Assert.Equal(1, b.Data.Characters["Hero"].Gathered[610610015]);
        Assert.Equal(2, b.Data.Characters["AltOne"].Gathered[610610015]);
    }

    [Fact]
    public void Weekly_reset_moves_the_week_to_last_week_once()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar"); b.OnGathered(610610015, 3); b.Tick("Hero", "ascension", 1); b.OnKina(1000);
        now = new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);   // next Wednesday after 07:00
        b.RollOver(); b.RollOver();
        var c = b.Data.Characters["Hero"];
        Assert.Empty(c.Gathered); Assert.Empty(c.Tasks);
        Assert.Equal(3, c.LastWeek!.Gathered[610610015]);
        Assert.Null(c.LastWeek.LastWeek);
        Assert.Equal(1000, c.KinaAtWeekStart);
    }

    [Fact]
    public void Ticks_stay_between_0_and_max_and_server_tasks_reset_daily()
    {
        var b = New();
        b.SetCharacter("Hero", "Templar");
        for (int i = 0; i < 9; i++) b.Tick(null, "duty", 1);
        b.Tick("Hero", "ascension", -1);
        Assert.Equal(5, b.Data.ServerTasks["duty"]);
        Assert.Equal(0, b.Data.Characters["Hero"].Tasks.GetValueOrDefault("ascension"));
        now = now.AddDays(1);
        b.RollOver();
        Assert.Equal(0, b.Data.ServerTasks.GetValueOrDefault("duty"));
    }

    [Fact]
    public void Store_round_trips_and_survives_a_corrupt_file()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "roster.json");
        var b = New(); b.SetCharacter("Hero", "Templar"); b.OnGathered(610610015, 2);
        RosterStore.Save(path, b.Data);
        Assert.Equal(2, RosterStore.Load(path).Characters["Hero"].Gathered[610610015]);
        File.WriteAllText(path, "{ not json");
        var fresh = RosterStore.Load(path);
        Assert.Empty(fresh.Characters);
        Assert.True(File.Exists(path + ".bad"));
        Assert.Contains(fresh.TaskDefs, t => t.Id == "duty");
    }
}

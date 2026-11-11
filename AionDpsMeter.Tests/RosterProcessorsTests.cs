using AionDpsMeter.Services.PacketProcessing.Processors;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Roster;
using Xunit;

public class RosterProcessorsTests
{
    [Fact]
    public void Processors_raise_roster_events()
    {
        var ev = new RosterEvents();
        GatherInfo? g = null; int morph = 0; long kina = 0; EnergyInfo? e = null;
        ev.Gathered += x => g = x; ev.MorphFinished += x => morph = x; ev.Kina += x => kina = x; ev.Energy += x => e = x;
        new ItemGatheredProcessor(ev).Process(new Packet { Data = Convert.FromHexString("1F338D013A82ED025E2B652401000000000000000000000000000000") });
        new MorphFinishedProcessor(ev).Process(new Packet { Data = Convert.FromHexString("2F8B8D0000013117DA0150053117DA0101000000010000000000000000000000000000000100000000000000") });
        new OdyleEnergyProcessor(ev).Process(new Packet { Data = Convert.FromHexString("130C61010C018793035A550128000000") });
        new InventoryUpdateProcessor(ev).Process(new Packet { Data = Convert.FromHexString("1B1B5601010000000000000000003F337037713240000000000000") });
        Assert.Equal(610610014, g!.Value.ItemId);
        Assert.Equal(31070001, morph);
        Assert.Equal(new EnergyInfo(90, 85, 40), e);
        Assert.Equal(4207217, kina);
    }
}

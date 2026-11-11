using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Roster;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // Control Panel inputs (see RosterParsers for the layouts). Display/tracking only — nothing is sent to the game.

    [PacketOpcode(PacketOpcodes.ItemGathered)]
    public sealed class ItemGatheredProcessor(RosterEvents events) : IOpcodeProcessor
    {
        public void Process(Packet packet) { if (RosterParsers.TryParseGather(packet.Data, out var g)) events.RaiseGathered(g); }
    }

    [PacketOpcode(PacketOpcodes.MorphFinished)]
    public sealed class MorphFinishedProcessor(RosterEvents events) : IOpcodeProcessor
    {
        public void Process(Packet packet) { if (RosterParsers.TryParseMorph(packet.Data, out var r)) events.RaiseMorph(r); }
    }

    [PacketOpcode(PacketOpcodes.OdyleEnergy)]
    public sealed class OdyleEnergyProcessor(RosterEvents events) : IOpcodeProcessor
    {
        public void Process(Packet packet) { if (RosterParsers.TryParseEnergy(packet.Data, out var e)) events.RaiseEnergy(e); }
    }

    [PacketOpcode(PacketOpcodes.InventoryUpdate)]
    public sealed class InventoryUpdateProcessor(RosterEvents events) : IOpcodeProcessor
    {
        private const int Kina = 930100031;
        public void Process(Packet packet)
        {
            if (RosterParsers.TryFindItemCount(packet.Data, Kina, out var n)) events.RaiseKina(n);
            events.RaiseInventory(packet.Data);
        }
    }
}

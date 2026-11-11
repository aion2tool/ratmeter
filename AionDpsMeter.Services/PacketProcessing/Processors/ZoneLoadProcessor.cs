using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Session;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    /// <summary>
    /// 0x4005 arrives once at the end of every loading screen (teleport, entering or leaving a
    /// dungeon/stronghold). Found by tracing opcodes around real zone changes on 2026-10-02:
    /// it fired 15 times in ~3 h, each time right after the loading-screen silence and just before
    /// the burst of new-area state (0x4001/0x4002/0x4301/0x3646...).
    /// </summary>
    [PacketOpcode(PacketOpcodes.ZoneLoaded)]
    internal sealed class ZoneLoadProcessor(ZoneChangeDetector detector) : IOpcodeProcessor
    {
        public void Process(Packet packet) => detector.OnZoneLoaded();
    }
}

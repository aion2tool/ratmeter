using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Session;
using System.Diagnostics;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.ServerTime)]
    public sealed class ServerTimePacketProcessor(CombatSessionManager sessionManager) : IOpcodeProcessor
    {
        private const int TimestampOffset = 5;
        private const long DotnetToUnixOffset = 62135596800000;

        private const long UnrealOffsetMs = 16_777_216L * 1000;

        private const int MinPing = 0;
        private const int MaxPing = 1000;

        public void Process(Packet packet)
        {
            if (packet.Data.Length < TimestampOffset + 8)
                throw new ArgumentException("Packet too short");

            long clientValue = BitConverter.ToInt64(packet.Data, TimestampOffset);

            long ms = CalcCounterPing(clientValue, packet.ReceivedAt);

            if (!IsValid(ms))
                ms = CalcLegacyPing(clientValue, packet.ReceivedAt);

            if (!IsValid(ms))
                ms = 0;

            sessionManager.FirePingUpdate((int)ms);
        }

        private static bool IsValid(long ms) => ms >= MinPing && ms <= MaxPing;

        private static long CalcCounterPing(long clientMs, long receivedAtUnixMs)
        {
            long nowCounterMs = CounterToMs(Stopwatch.GetTimestamp(), Stopwatch.Frequency) + UnrealOffsetMs;
            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();


            long receivedCounterMs = nowCounterMs - (nowUnixMs - receivedAtUnixMs);

            return receivedCounterMs - clientMs;
        }

        private static long CalcLegacyPing(long dotnetMs, long receivedAtUnixMs)
        {
            long clientUnixMs = dotnetMs - DotnetToUnixOffset;
            return receivedAtUnixMs - clientUnixMs;
        }

        private static long CounterToMs(long counter, long freq)
            => (counter / freq) * 1000 + (counter % freq) * 1000 / freq;
    }
}

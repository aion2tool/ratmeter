using AionDpsMeter.Services.Extensions;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.FieldBosses;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    /// <summary>
    /// Field boss list (01 91), sent about once a second while the world map is open. Layout, worked out from a
    /// capture checked against the in-game list (all 10 countdowns matched to the second):
    ///   [len varint][01 91][00 00][u32 map id (1110 = Altgard)][u8 count]
    ///   per boss: [u8 0 = waiting / 1 = up][spawn id varint][up: 3 x float position][u64 unix ms]
    ///   where the time is the respawn time while waiting, the spawn time while up.
    /// A few entries (3 of 24 seen) have one unexplained byte before the time, so the time is taken at the
    /// expected spot or one byte later, whichever is a plausible time (within 2 days of now).
    /// </summary>
    [PacketOpcode(PacketOpcodes.FieldBossList)]
    public sealed class FieldBossListProcessor(FieldBossBoard board, ILogger<FieldBossListProcessor> logger) : IOpcodeProcessor
    {
        private int logged;

        public void Process(Packet packet)
        {
            var list = Parse(packet.Data, DateTime.UtcNow, out int mapId);
            if (list is null || list.Count == 0) return;
            if (logged < 3)
            {
                logged++;
                logger.LogInformation("[BOSSES] Map {Map}: {Count} field bosses ({Up} up, next respawn {Next:HH:mm:ss})", mapId, list.Count,
                    list.Count(e => e.IsUp), list.Where(e => !e.IsUp).Select(e => (DateTime?)e.TimeUtc.ToLocalTime()).Min());
            }
            board.Set(mapId, list);
        }

        public static List<FieldBossEntry>? Parse(byte[] data, DateTime nowUtc, out int mapId)
        {
            mapId = 0;
            var len = data.ReadVarInt();
            int p = len.Length + 2 + 2;          // length, opcode, 2 zero bytes
            if (p + 6 > data.Length) return null;
            mapId = BitConverter.ToInt32(data, p); p += 4;
            int count = data[p++];
            if (count == 0) return null;

            long minMs = new DateTimeOffset(nowUtc.AddDays(-2)).ToUnixTimeMilliseconds();
            long maxMs = new DateTimeOffset(nowUtc.AddDays(2)).ToUnixTimeMilliseconds();
            bool Plausible(int at, out long ms)
            {
                ms = at >= 0 && at + 8 <= data.Length ? BitConverter.ToInt64(data, at) : 0;
                return ms >= minMs && ms <= maxMs;
            }
            bool TryVarint(int at, out int value, out int end)
            {
                value = 0; end = at;
                if (at >= data.Length) return false;
                var v = data.ReadVarInt(at);
                if (v.Length <= 0) return false;
                value = v.Value; end = at + v.Length;
                return true;
            }

            var list = new List<FieldBossEntry>(count);
            for (int k = 0; k < count && p < data.Length; k++)
            {
                byte state = data[p];
                if (state > 1 || !TryVarint(p + 1, out int id, out int q)) break; // unknown shape: keep what was read
                (float, float, float)? pos = null;
                if (state == 1)
                {
                    if (q + 12 > data.Length) break;
                    pos = (BitConverter.ToSingle(data, q), BitConverter.ToSingle(data, q + 4), BitConverter.ToSingle(data, q + 8));
                    q += 12;
                }
                long ms;
                if (Plausible(q, out ms)) p = q + 8;
                else if (Plausible(q + 1, out ms)) p = q + 9;
                else break;
                list.Add(new FieldBossEntry(id, state == 1, FromMs(ms), pos));
            }
            return list;
        }

        private static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }
}

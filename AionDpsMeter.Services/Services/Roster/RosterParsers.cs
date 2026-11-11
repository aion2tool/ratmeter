using System.Buffers.Binary;

namespace AionDpsMeter.Services.Services.Roster
{
    public readonly record struct GatherInfo(int ItemId, int Count, int Bonus, bool Rare);
    public readonly record struct EnergyInfo(int Basic, int Charged, int Added);

    /// <summary>
    /// Game messages for the Control Panel, worked out from the user's recordings of 2026-10-22 (see the spec).
    /// Each takes the whole decoded message: [length varint][2 opcode bytes][body]. Never throws: a layout the
    /// parser doesn't recognise (e.g. after a game patch) returns false.
    /// </summary>
    public static class RosterParsers
    {
        public const byte GatherA = 0x33, GatherB = 0x8D;   // 33 8D  item gathered
        public const byte MorphA = 0x8B, MorphB = 0x8D;     // 8B 8D  Substance Morph finished
        public const byte InvA = 0x1B, InvB = 0x56;         // 1B 56  inventory / currency update
        public const byte EnergyA = 0x0C, EnergyB = 0x61;   // 0C 61  Odyle energy changed

        private const int Kina = 930100031;

        /// <summary>[flag: 1 = rare][0x3A][node varint][u32 item][u32 count][u32 bonus]</summary>
        public static bool TryParseGather(ReadOnlySpan<byte> msg, out GatherInfo g)
        {
            g = default;
            if (!Body(msg, GatherA, GatherB, out var b) || b.Length < 2 || b[0] > 2 || b[1] != 0x3A) return false;
            bool rare = b[0] == 1;
            int p = 2;
            if (!VarInt(b, ref p, out _) || p + 12 > b.Length) return false;
            int item = BinaryPrimitives.ReadInt32LittleEndian(b[p..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(b[(p + 4)..]);
            int bonus = BinaryPrimitives.ReadInt32LittleEndian(b[(p + 8)..]);
            if (item <= 0 || count < 0 || bonus < 0 || count > 10_000 || bonus > 10_000) return false;
            g = new GatherInfo(item, count, bonus, rare);
            return true;
        }

        /// <summary>[00 00 01][u32 recipe]…</summary>
        public static bool TryParseMorph(ReadOnlySpan<byte> msg, out int recipeId)
        {
            recipeId = 0;
            if (!Body(msg, MorphA, MorphB, out var b) || b.Length < 7 || b[0] != 0 || b[1] != 0 || b[2] != 1) return false;
            recipeId = BinaryPrimitives.ReadInt32LittleEndian(b[3..]);
            return recipeId > 0;
        }

        /// <summary>[01 0C 01][varint ?][varint basic][varint charged][01][u32 added]</summary>
        public static bool TryParseEnergy(ReadOnlySpan<byte> msg, out EnergyInfo e)
        {
            e = default;
            if (!Body(msg, EnergyA, EnergyB, out var b) || b.Length < 3 || b[0] != 0x01 || b[1] != 0x0C || b[2] != 0x01) return false;
            int p = 3;
            if (!VarInt(b, ref p, out _) || !VarInt(b, ref p, out int basic) || !VarInt(b, ref p, out int charged)) return false;
            p += 1;
            if (p + 4 > b.Length) return false;
            int added = BinaryPrimitives.ReadInt32LittleEndian(b[p..]);
            if (basic < 0 || charged < 0 || basic > 100_000 || charged > 100_000) return false;
            e = new EnergyInfo(basic, charged, added);
            return true;
        }

        /// <summary>
        /// Inventory update: finds the entry for <paramref name="itemId"/> and reads the count after it.
        /// Kina (930100031) is 8 bytes, items 4. Entries are not fixed-size, so this looks for the id itself.
        /// </summary>
        public static bool TryFindItemCount(ReadOnlySpan<byte> msg, int itemId, out long count)
        {
            count = 0;
            if (!Body(msg, InvA, InvB, out var b)) return false;
            Span<byte> id = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(id, itemId);
            int at = b.IndexOf(id);
            if (at < 0 || at + 12 > b.Length) return false;
            count = itemId == Kina
                ? BinaryPrimitives.ReadInt64LittleEndian(b[(at + 4)..])
                : BinaryPrimitives.ReadUInt32LittleEndian(b[(at + 4)..]);
            return count >= 0 && count < 1_000_000_000_000;
        }

        internal static bool Body(ReadOnlySpan<byte> msg, byte op0, byte op1, out ReadOnlySpan<byte> body)
        {
            body = default;
            int p = 0;
            if (!VarInt(msg, ref p, out _) || p + 2 > msg.Length || msg[p] != op0 || msg[p + 1] != op1) return false;
            body = msg[(p + 2)..];
            return true;
        }

        private static bool VarInt(ReadOnlySpan<byte> b, ref int p, out int value)
        {
            value = 0;
            for (int shift = 0; shift < 35 && p < b.Length; shift += 7)
            {
                byte x = b[p++];
                value |= (x & 0x7F) << shift;
                if ((x & 0x80) == 0) return true;
            }
            return false;
        }
    }
}

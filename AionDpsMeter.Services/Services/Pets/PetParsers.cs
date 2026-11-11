using System.Buffers.Binary;
using AionDpsMeter.Services.Services.Roster;

namespace AionDpsMeter.Services.Services.Pets
{
    public readonly record struct ItemEntry(int ItemId, byte Flag, uint Count);

    /// <summary>
    /// Pet souls are inventory items: a drop is an inventory update (1B 56) entry with flag 00 and count 1,
    /// absorbed by the pet ~0.1 s later (same item, flag 02, count 0). In every entry the flag sits 10 bytes
    /// before the u32 item id (then a 00 byte); the u32 count follows the id. Entries differ in length, so
    /// this scans for the ids the caller wants. Never throws.
    /// </summary>
    public static class PetParsers
    {
        public const byte Added = 0, Changed = 1, Removed = 2;

        public static List<ItemEntry> FindItems(ReadOnlySpan<byte> msg, Func<int, bool> wanted)
        {
            var list = new List<ItemEntry>();
            if (!RosterParsers.Body(msg, RosterParsers.InvA, RosterParsers.InvB, out var b)) return list;
            for (int at = 11; at + 8 <= b.Length; at++)
            {
                int id = BinaryPrimitives.ReadInt32LittleEndian(b[at..]);
                if (!wanted(id)) continue;
                byte flag = b[at - 10];
                if (flag > Removed || b[at - 9] != 0) continue;
                list.Add(new ItemEntry(id, flag, BinaryPrimitives.ReadUInt32LittleEndian(b[(at + 4)..])));
                at += 7;
            }
            return list;
        }
    }
}

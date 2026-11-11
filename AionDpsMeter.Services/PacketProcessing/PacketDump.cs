using System.Text;
using AionDpsMeter.Services.Extensions;

namespace AionDpsMeter.Services.PacketProcessing
{
    /// <summary>
    /// Developer tool (Settings → Data → Record Game Messages): for a few minutes, every decoded game message
    /// (after decompression) is written to Logs\messages-yyyyMMdd-HHmmss.txt as "time opcode length hex".
    /// Used to find which message carries something new (e.g. the field boss respawn list). Stays on this PC.
    /// </summary>
    public static class PacketDump
    {
        private static readonly Lock gate = new();
        private static StreamWriter? writer;
        private static DateTime until;

        public static bool IsRecording { get { lock (gate) return writer is not null && DateTime.Now < until; } }
        public static DateTime RecordingUntil { get { lock (gate) return until; } }
        public static string? CurrentFile { get; private set; }

        public static void Start(TimeSpan length)
        {
            lock (gate)
            {
                writer?.Dispose();
                string dir = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(dir);
                CurrentFile = Path.Combine(dir, $"messages-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                writer = new StreamWriter(CurrentFile, false, Encoding.ASCII) { AutoFlush = true };
                until = DateTime.Now + length;
                writer.WriteLine($"# game messages from {DateTime.Now:yyyy-MM-dd HH:mm:ss} for {length.TotalMinutes:0} min: time opcode length hex");
            }
        }

        /// <summary>Called for every decoded message (packet thread).</summary>
        public static void Write(byte[] data)
        {
            if (writer is null) return; // fast path while not recording
            lock (gate)
            {
                if (writer is null) return;
                if (DateTime.Now >= until)
                {
                    writer.Dispose();
                    writer = null;
                    return;
                }
                var len = data.ReadVarInt();
                string opcode = data.Length >= len.Length + 2 ? $"{data[len.Length]:X2}{data[len.Length + 1]:X2}" : "????";
                writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {opcode} {data.Length} {Convert.ToHexString(data)}");
            }
        }
    }
}

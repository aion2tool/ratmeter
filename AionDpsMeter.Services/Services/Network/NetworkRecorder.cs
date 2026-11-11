using System.Globalization;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.Services.Services.Network
{
    /// <summary>
    /// Writes the game connection's numbers once a second to Logs\network-yyyyMMdd.csv, for finding
    /// out what happened during a lag spike: game ping (from the game's own server-time packets),
    /// traffic, packets/s, and new resent / missed / dropped packets in that second.
    /// </summary>
    public sealed class NetworkRecorder : IDisposable
    {
        private readonly NetworkStats stats;
        private readonly ILogger<NetworkRecorder> logger;
        private readonly Timer timer;
        private NetworkSnapshot? last;
        private DateTime lastAt;
        private DateTime lastPingSeen = DateTime.MinValue;

        public NetworkRecorder(NetworkStats stats, ILogger<NetworkRecorder> logger)
        {
            this.stats = stats;
            this.logger = logger;
            timer = new Timer(_ => Record(), null, 1000, 1000);
        }

        private void Record()
        {
            try
            {
                var now = DateTime.Now;
                var snap = stats.TakeSnapshot();
                if (last is null) { last = snap; lastAt = now; return; }
                double secs = Math.Max(0.2, (now - lastAt).TotalSeconds);

                // Ping samples that arrived since the previous line (the game sends them every few seconds).
                var fresh = snap.Pings.Where(p => p.At > lastPingSeen).ToList();
                if (fresh.Count > 0) lastPingSeen = fresh[^1].At;
                string ping = fresh.Count > 0 ? fresh.Max(p => p.Ms).ToString(CultureInfo.InvariantCulture) : "";

                string line = string.Join(",",
                    now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    ping,
                    ((snap.DownBytes - last.DownBytes) / 1024.0 / secs).ToString("F1", CultureInfo.InvariantCulture),
                    ((snap.UpBytes - last.UpBytes) / 1024.0 / secs).ToString("F1", CultureInfo.InvariantCulture),
                    ((snap.DownPackets + snap.UpPackets - last.DownPackets - last.UpPackets) / secs).ToString("F0", CultureInfo.InvariantCulture),
                    snap.Resent - last.Resent,
                    snap.Missed - last.Missed,
                    snap.Dropped - last.Dropped,
                    snap.ServerEndpoint ?? "");

                string dir = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"network-{now:yyyyMMdd}.csv");
                if (!File.Exists(file))
                {
                    File.AppendAllText(file, "time,game_ping_ms,down_kbs,up_kbs,packets_s,resent,missed,dropped,server" + Environment.NewLine);
                    DeleteOldFiles(dir);
                }
                File.AppendAllText(file, line + Environment.NewLine);

                last = snap;
                lastAt = now;
            }
            catch (Exception ex)
            {
                logger.LogDebug("Network recorder: {Error}", ex.Message);
            }
        }

        /// <summary>Keeps the newest <see cref="KeepFiles"/> days, like the text logs (retainedFileCountLimit).</summary>
        private const int KeepFiles = 20;

        private static void DeleteOldFiles(string dir)
        {
            // yyyyMMdd in the name sorts by date.
            foreach (var old in Directory.GetFiles(dir, "network-????????.csv").OrderDescending().Skip(KeepFiles))
            {
                try { File.Delete(old); } catch (IOException) { /* open elsewhere: next day */ }
            }
        }

        public void Dispose() => timer.Dispose();
    }
}

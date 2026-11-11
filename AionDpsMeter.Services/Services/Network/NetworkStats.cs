using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.Services.Services.Network
{
    /// <summary>
    /// Live numbers for the network overlay, fed by the packet capture (game connection only)
    /// and by the game's own server-time packets (ping). Thread-safe; read with <see cref="TakeSnapshot"/>.
    /// </summary>
    public sealed class NetworkStats
    {
        private static readonly TimeSpan PingHistory = TimeSpan.FromSeconds(120);

        private long downBytes, upBytes, downPackets, upPackets, resent, missed, dropped;
        private readonly Lock gate = new();
        private readonly Queue<(DateTime At, int Ms)> pings = new();
        private volatile string? serverEndpoint;

        // Server stalls: the server sent nothing for StallThreshold while your client kept sending
        // (so it isn't a loading screen or the game closing). Reported when data resumes.
        private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MaxStall = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StallHistory = TimeSpan.FromMinutes(30);
        private long lastDownMs;     // packet thread only
        private long upAtLastDown;   // packet thread only
        private readonly List<LagStall> stalls = new();
        private readonly ILogger<NetworkStats> logger;

        /// <summary>A stall just ended (raised on the packet thread).</summary>
        public event Action<LagStall>? StallDetected;

        public NetworkStats(CombatSessionManager sessionManager, ILogger<NetworkStats> logger)
        {
            this.logger = logger;
            sessionManager.PingUpdated += (_, ms) => RecordPing(ms);
        }

        public void RecordDown(int bytes)
        {
            Interlocked.Increment(ref downPackets);
            Interlocked.Add(ref downBytes, bytes);

            // Bare TCP ACKs (no payload) keep flowing during a stall; only game data counts.
            if (bytes <= 0) return;
            long nowMs = Environment.TickCount64;
            long up = Interlocked.Read(ref upPackets);
            long gapMs = nowMs - lastDownMs;
            // Client still actively sending (≥ 2 packets/s, normal play sends far more) and not a loading
            // screen or reconnect (those go quiet for longer than MaxStall).
            if (lastDownMs > 0 && gapMs >= StallThreshold.TotalMilliseconds && gapMs <= MaxStall.TotalMilliseconds &&
                up - upAtLastDown >= gapMs / 500)
            {
                var length = TimeSpan.FromMilliseconds(gapMs);
                var stall = new LagStall(DateTime.Now - length, length);
                lock (gate)
                {
                    stalls.Add(stall);
                    stalls.RemoveAll(s => DateTime.Now - s.Start > StallHistory);
                }
                logger.LogWarning("[LAG] Game server sent nothing for {Secs:F1}s while your client kept sending (started {Start:HH:mm:ss})",
                    length.TotalSeconds, stall.Start);
                try { StallDetected?.Invoke(stall); } catch (Exception ex) { logger.LogError(ex, "StallDetected handler failed"); }
            }
            lastDownMs = nowMs;
            upAtLastDown = up;
        }

        /// <summary>Server stalls that started in [from, to].</summary>
        public IReadOnlyList<LagStall> GetStalls(DateTime from, DateTime to)
        {
            lock (gate) return stalls.Where(s => s.Start >= from && s.Start <= to).ToList();
        }

        public LagStall? LastStall
        {
            get { lock (gate) return stalls.Count > 0 ? stalls[^1] : null; }
        }
        public void RecordUp(int bytes) { Interlocked.Increment(ref upPackets); Interlocked.Add(ref upBytes, bytes); }
        /// <summary>The server sent a segment again (it thought the first copy was lost).</summary>
        public void RecordResent() => Interlocked.Increment(ref resent);
        /// <summary>A segment never reached the capture (sequence gap).</summary>
        public void RecordMissed() => Interlocked.Increment(ref missed);
        /// <summary>The meter itself fell behind and skipped a packet.</summary>
        public void RecordDropped() => Interlocked.Increment(ref dropped);
        public void SetServer(string endpoint) => serverEndpoint = endpoint;

        public void RecordPing(int ms)
        {
            if (ms <= 0) return; // 0 = the game packet couldn't be read as a ping
            var now = DateTime.Now;
            lock (gate)
            {
                pings.Enqueue((now, ms));
                while (pings.Count > 0 && now - pings.Peek().At > PingHistory) pings.Dequeue();
            }
        }

        public NetworkSnapshot TakeSnapshot()
        {
            (DateTime, int)[] pingCopy;
            lock (gate) { pingCopy = pings.ToArray(); }
            return new NetworkSnapshot(
                Interlocked.Read(ref downBytes), Interlocked.Read(ref upBytes),
                Interlocked.Read(ref downPackets), Interlocked.Read(ref upPackets),
                Interlocked.Read(ref resent), Interlocked.Read(ref missed), Interlocked.Read(ref dropped),
                serverEndpoint, pingCopy);
        }
    }

    public sealed record LagStall(DateTime Start, TimeSpan Length);

    public sealed record NetworkSnapshot(
        long DownBytes, long UpBytes, long DownPackets, long UpPackets,
        long Resent, long Missed, long Dropped,
        string? ServerEndpoint, IReadOnlyList<(DateTime At, int Ms)> Pings);
}

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Recording
{
    /// <summary>
    /// Pre-roll: a fight is only detected after a few hits, so a normal recording misses the pull.
    /// With pre-roll on (and AION 2 running) ffmpeg records all the time into 5 s segments in
    /// "&lt;folder&gt;\.buffer"; old segments are deleted as new ones arrive. A fight just marks its start;
    /// when it ends, the segments from <c>RecorderPreRollSeconds</c> before the start up to the end
    /// are joined and trimmed without re-encoding into the usual .mp4.
    /// </summary>
    public sealed partial class BossRecorder
    {
        private const int SegmentSeconds = 5;
        private const string SegmentFormat = "yyyyMMdd-HHmmss";
        private static readonly TimeSpan GameCheckEvery = TimeSpan.FromSeconds(10);

        private Process? buffer;
        private AudioFeed? bufferAudio;
        private bool bufferFight;
        private DateTime lastGameCheck = DateTime.MinValue;
        private bool gameRunning;
        // Segments from this time on are being (or about to be) turned into a recording: never pruned.
        private DateTime keepSegmentsFrom = DateTime.MaxValue;

        private string BufferDir => Path.Combine(settings.RecorderFolder, ".buffer");
        private TimeSpan PreRoll => TimeSpan.FromSeconds(settings.RecorderPreRollSeconds);

        private void TickBuffer(DateTime now)
        {
            if (buffer is { HasExited: true })
            {
                logger.LogWarning("Pre-roll buffer ffmpeg exited ({Code}); retrying in {Delay}s", buffer.ExitCode, StartRetryDelay.TotalSeconds);
                ReleaseBuffer();
                if (bufferFight) { bufferFight = false; keepSegmentsFrom = DateTime.MaxValue; StateChanged?.Invoke(); }
                startBlockedUntil = now + StartRetryDelay;
                Fail("Pre-roll buffer stopped unexpectedly, see log");
                return;
            }

            bool enabled = settings.RecorderPreRoll && (settings.RecorderEnabled || settings.RecorderPvp);
            if (enabled && now - lastGameCheck >= GameCheckEvery)
            {
                lastGameCheck = now;
                gameRunning = Process.GetProcessesByName("AION2").Length > 0;
            }
            bool want = enabled && gameRunning;

            // A normal recording in progress keeps the GPU encoder; the buffer starts after it.
            if (want && buffer is null && ffmpeg is null && now >= startBlockedUntil) StartBuffer();
            else if (!want && buffer is not null && !bufferFight) StopBuffer(wait: false);

            if (buffer is not null) PruneSegments(now);
        }

        private void StartBuffer()
        {
            ffmpegPath ??= FindFfmpeg();
            if (ffmpegPath is null) { Fail("ffmpeg not found (install with: winget install Gyan.FFmpeg)"); return; }
            try
            {
                Directory.CreateDirectory(BufferDir);
                ClearBuffer();
                string pattern = Path.Combine(BufferDir, "%Y%m%d-%H%M%S.mkv");
                // A keyframe every segment boundary so every segment can be cut and joined cleanly.
                buffer = LaunchCapture(
                    $"-force_key_frames \"expr:gte(t,n_forced*{SegmentSeconds})\" -f segment -segment_time {SegmentSeconds} " +
                    $"-reset_timestamps 1 -strftime 1 \"{pattern}\"", out bufferAudio);
                logger.LogInformation("Pre-roll buffer started ({Secs}s kept) -> {Dir}", settings.RecorderPreRollSeconds, BufferDir);
            }
            catch (Exception ex)
            {
                ReleaseBuffer();
                startBlockedUntil = DateTime.Now + StartRetryDelay;
                Fail("Could not start the pre-roll buffer: " + ex.Message);
            }
        }

        private void StopBuffer(bool wait)
        {
            var process = buffer;
            var feed = bufferAudio;
            if (process is null) return;
            buffer = null;
            bufferAudio = null;
            var stoppedAt = DateTime.Now;
            var stop = Task.Run(() =>
            {
                try { process.StandardInput.Write('q'); process.StandardInput.Flush(); } catch { /* already gone */ }
                if (!process.WaitForExit(10_000)) try { process.Kill(); } catch { }
                feed?.Dispose();
                // Only this buffer's segments: a new buffer may already be writing fresh ones.
                if (keepSegmentsFrom == DateTime.MaxValue) ClearBuffer(before: stoppedAt);
            });
            if (wait) stop.Wait(TimeSpan.FromSeconds(15));
            logger.LogInformation("Pre-roll buffer stopped");
        }

        /// <summary>Forget a dead buffer process without the clean-stop dance.</summary>
        private void ReleaseBuffer()
        {
            try { bufferAudio?.Dispose(); } catch { }
            bufferAudio = null;
            buffer = null;
        }

        private void BeginBufferedFight(string label, DateTime lastHit, bool boss)
        {
            BeginFight(label, lastHit, boss);
            bufferFight = true;
            keepSegmentsFrom = recordingStarted - PreRoll - TimeSpan.FromSeconds(SegmentSeconds * 2);
            logger.LogInformation("Recording started (pre-roll {Secs}s): {Label}", settings.RecorderPreRollSeconds, label);
            StateChanged?.Invoke();
        }

        private void EndBufferedFight(DateTime endAt, bool wait)
        {
            var fightStart = recordingStarted;
            var clipStart = fightStart - PreRoll;
            var boss = bossName;
            var killed = killedAt is not null;
            var lastAction = lastBossHit > lastPvpHit ? lastBossHit : lastPvpHit;
            var fightLength = lastAction - recordingStarted;
            var minLength = hadBoss ? MinFight : MinPvp;
            bufferFight = false;
            StateChanged?.Invoke();

            if (fightLength < minLength)
            {
                keepSegmentsFrom = DateTime.MaxValue;
                logger.LogInformation("Recording discarded (fight shorter than {Min}s)", minLength.TotalSeconds);
                return;
            }

            // App closing: stop the buffer first so the last segment is complete.
            if (wait) StopBuffer(wait: true);

            var finish = Task.Run(() =>
            {
                try
                {
                    // The segment holding the end of the fight must be closed: wait for the next one (or the buffer to stop).
                    var deadline = DateTime.Now + TimeSpan.FromSeconds(SegmentSeconds * 3);
                    while (DateTime.Now < deadline && buffer is not null && !Segments().Any(s => s.Start > endAt))
                        Thread.Sleep(250);
                    BuildFromSegments(clipStart, fightStart, endAt, boss, killed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Building the pre-roll recording failed");
                }
                finally
                {
                    keepSegmentsFrom = DateTime.MaxValue;
                }
            });
            if (wait) finish.Wait(TimeSpan.FromSeconds(30));
        }

        private void BuildFromSegments(DateTime clipStart, DateTime fightStart, DateTime endAt, string boss, bool killed)
        {
            var all = Segments().ToList();
            // From the last segment that starts at/before the clip start, through the one holding the end.
            int first = all.FindLastIndex(s => s.Start <= clipStart);
            if (first < 0) first = 0; // buffer started after clipStart: begin with what exists
            var parts = all.Skip(first).TakeWhile(s => s.Start <= endAt).ToList();
            if (parts.Count == 0)
            {
                logger.LogWarning("Pre-roll recording: no buffer segments between {From} and {To}", clipStart, endAt);
                return;
            }

            var actualStart = clipStart > parts[0].Start ? clipStart : parts[0].Start;
            var offset = actualStart - parts[0].Start;
            var length = endAt - actualStart;
            if (length <= TimeSpan.Zero) length = TimeSpan.FromSeconds(1);

            string list = Path.Combine(BufferDir, $"join-{DateTime.Now.Ticks}.txt");
            File.WriteAllLines(list, parts.Select(p => $"file '{p.Path.Replace("'", "'\\''")}'"), new UTF8Encoding(false));
            string mp4 = Path.Combine(settings.RecorderFolder, RecordingName(actualStart, length, boss, killed));
            string inv(TimeSpan t) => t.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);
            try
            {
                var psi = new ProcessStartInfo(ffmpegPath!,
                    $"-hide_banner -loglevel error -y -f concat -safe 0 -ss {inv(offset)} -i \"{list}\" -t {inv(length)} -c copy -movflags +faststart \"{mp4}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                using var join = Process.Start(psi)!;
                string err = join.StandardError.ReadToEnd();
                join.WaitForExit();
                if (join.ExitCode == 0 && File.Exists(mp4))
                {
                    logger.LogInformation("Boss recording saved (with {Secs:F0}s pre-roll): {File}", (fightStart - actualStart).TotalSeconds, mp4);
                    RunHousekeeping(recoverLeftovers: false);
                }
                else logger.LogWarning("Joining pre-roll segments failed ({Code}): {Error}", join.ExitCode, err.Trim());
            }
            finally
            {
                try { File.Delete(list); } catch (IOException) { }
            }
        }

        /// <summary>Buffer segments, oldest first; the name is the local time the segment started.</summary>
        private IEnumerable<(DateTime Start, string Path)> Segments()
        {
            if (!Directory.Exists(BufferDir)) yield break;
            foreach (var f in Directory.GetFiles(BufferDir, "*.mkv").OrderBy(f => f, StringComparer.Ordinal))
            {
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), SegmentFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                    yield return (start, f);
            }
        }

        private void PruneSegments(DateTime now)
        {
            var cutoff = now - PreRoll - TimeSpan.FromSeconds(SegmentSeconds * 3);
            if (keepSegmentsFrom < cutoff) cutoff = keepSegmentsFrom;
            foreach (var (start, path) in Segments())
            {
                if (start >= cutoff) break;
                try { File.Delete(path); } catch (IOException) { /* still open: next tick */ }
            }
        }

        private void ClearBuffer(DateTime? before = null)
        {
            try
            {
                if (!Directory.Exists(BufferDir)) return;
                foreach (var f in Directory.GetFiles(BufferDir))
                {
                    if (before is { } b && File.GetCreationTime(f) >= b) continue;
                    try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
        }
    }
}

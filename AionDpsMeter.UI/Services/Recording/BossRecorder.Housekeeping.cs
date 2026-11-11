using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Recording
{
    /// <summary>
    /// Keeps the recordings folder in check:
    ///  - on start: stops ffmpeg left over from an earlier meter session and turns its unfinished
    ///    .mkv into a "(recovered)" .mp4;
    ///  - on start, after every recording and every hour: deletes the OLDEST recordings while the
    ///    folder is over the size limit or the drive has less than the minimum free space.
    /// Only *.mp4 / *.mkv files in the recordings folder are ever deleted, never the newest one.
    /// </summary>
    public sealed partial class BossRecorder
    {
        private static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(30);
        private const long MinFreeBytes = 20L * 1024 * 1024 * 1024;   // keep at least 20 GB free on the drive
        private readonly System.Windows.Threading.DispatcherTimer housekeepingTimer = new() { Interval = TimeSpan.FromHours(1) };
        private int housekeepingRunning;

        private void StartHousekeeping()
        {
            housekeepingTimer.Tick += (_, _) => RunHousekeeping(recoverLeftovers: false);
            housekeepingTimer.Start();
            RunHousekeeping(recoverLeftovers: true);
        }

        private void RunHousekeeping(bool recoverLeftovers)
        {
            if (Interlocked.Exchange(ref housekeepingRunning, 1) == 1) return;
            string folder = settings.RecorderFolder;
            long maxBytes = Math.Max(5, settings.RecorderMaxGB) * 1024L * 1024 * 1024;
            int keepDays = settings.RecorderRetentionDays;
            string? current = tempFile; // never touch the file being recorded
            Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(folder)) return;
                    if (recoverLeftovers) RecoverLeftovers(folder);
                    if (keepDays > 0) DeleteOlderThan(folder, DateTime.Now.AddDays(-keepDays), current);
                    TrimFolder(folder, maxBytes, current);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Recording housekeeping failed"); }
                finally { Interlocked.Exchange(ref housekeepingRunning, 0); }
            });
        }

        /// <summary>ffmpeg still writing into our folder from a previous meter session: stop it, keep its video.</summary>
        private void RecoverLeftovers(string folder)
        {
            // A leftover = an ffmpeg from the same ffmpeg install that started before this meter did
            // (this meter hasn't recorded anything yet when this runs).
            ffmpegPath ??= FindFfmpeg();
            DateTime meterStarted = Process.GetCurrentProcess().StartTime;
            foreach (var p in Process.GetProcessesByName("ffmpeg"))
            {
                try
                {
                    if (p.StartTime >= meterStarted) continue;
                    string? exe = p.MainModule?.FileName;
                    if (ffmpegPath is null || exe is null || !string.Equals(Path.GetDirectoryName(exe), Path.GetDirectoryName(Path.GetFullPath(ffmpegPath)), StringComparison.OrdinalIgnoreCase))
                        continue;
                    p.Kill();
                    logger.LogWarning("Stopped a leftover recording ffmpeg (pid {Pid}, started {Start}) from an earlier meter session", p.Id, p.StartTime);
                }
                catch (Exception) { /* exited or not accessible */ }
            }

            Thread.Sleep(1500); // let the killed ffmpeg release its file
            foreach (var mkv in Directory.EnumerateFiles(folder, "recording-*.mkv"))
            {
                if (ffmpegPath is null && (ffmpegPath = FindFfmpeg()) is null) return;
                string mp4 = Path.Combine(folder, Path.GetFileNameWithoutExtension(mkv) + " (recovered).mp4");
                try
                {
                    // Already converted on an earlier start (the meter closed before cleaning up): just drop the .mkv.
                    if (File.Exists(mp4) && new FileInfo(mp4).Length >= new FileInfo(mkv).Length * 0.95)
                    {
                        File.Delete(mkv);
                        continue;
                    }
                    var psi = new ProcessStartInfo(ffmpegPath, $"-hide_banner -loglevel error -y -i \"{mkv}\" -c copy -movflags +faststart \"{mp4}\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    using var remux = Process.Start(psi)!;
                    ChildProcessJob.Add(remux); // ends with the meter
                    remux.StandardError.ReadToEnd();
                    remux.WaitForExit();
                    if (remux.ExitCode == 0 && File.Exists(mp4) && new FileInfo(mp4).Length > 0)
                    {
                        File.Delete(mkv);
                        logger.LogInformation("Recovered unfinished recording: {File}", mp4);
                    }
                }
                catch (IOException) { /* still in use: next start tries again */ }
            }
        }

        /// <summary>"2026-10-08 15-29-02 Boss (kill, 0m47s).mp4" or "recording-20261008-152902[ (recovered)].mkv/.mp4".</summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2} .+|recording-\d{8}-\d{6}.*)\.(mp4|mkv)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex OwnRecordingName();

        /// <summary>"Delete videos after N days" (only files this recorder named).</summary>
        private void DeleteOlderThan(string folder, DateTime cutoff, string? current)
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles())
            {
                if (!OwnRecordingName().IsMatch(f.Name) || f.LastWriteTime >= cutoff) continue;
                if (current is not null && f.FullName.Equals(current, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    f.Delete();
                    logger.LogInformation("Deleted recording {File} (older than {Days} days)", f.Name, settings.RecorderRetentionDays);
                }
                catch (IOException) { /* in use: next hour */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Deletes oldest recordings until the folder fits the size limit and the drive keeps its free space.</summary>
        private void TrimFolder(string folder, long maxBytes, string? current)
        {
            var files = new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
                // Only files this recorder named, so pointing the folder at e.g. Videos never deletes other videos.
                .Where(f => OwnRecordingName().IsMatch(f.Name))
                .Where(f => current is null || !f.FullName.Equals(current, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            if (files.Count <= 1) return;
            var newest = files[^1];
            long total = files.Sum(f => f.Length);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);

            foreach (var f in files)
            {
                if (f == newest) break;
                bool overSize = total > maxBytes;
                bool lowSpace = drive.AvailableFreeSpace < MinFreeBytes;
                if (!overSize && !lowSpace) break;
                try
                {
                    long size = f.Length;
                    f.Delete();
                    total -= size;
                    drive = new DriveInfo(drive.Name);
                    logger.LogInformation("Deleted old recording {File} ({SizeMb:F0} MB) - {Reason}",
                        f.Name, size / 1048576.0, overSize ? $"folder over {maxBytes / 1073741824} GB" : "drive low on space");
                }
                catch (IOException) { /* in use: skip */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

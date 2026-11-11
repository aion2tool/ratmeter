using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AionDpsMeter.UI.Services.Recording
{
    /// <summary>
    /// Records the screen while a boss fight is going on. Capture (Desktop Duplication) and
    /// encoding (NVENC AV1) both run on the GPU through ffmpeg, so CPU cost is tiny. Game sound
    /// comes from WASAPI loopback and is streamed into ffmpeg through a named pipe.
    /// The file is written as .mkv while recording (survives crashes), then trimmed to the end
    /// of the fight and remuxed to .mp4 without re-encoding.
    /// </summary>
    public sealed partial class BossRecorder
    {
        private static readonly TimeSpan StartWindow = TimeSpan.FromSeconds(10); // boss hit this recently = fight on
        private static readonly TimeSpan EndGap = TimeSpan.FromSeconds(20);      // no boss hits this long = fight over
        private static readonly TimeSpan Tail = TimeSpan.FromSeconds(4);         // kept after the last hit / kill
        private static readonly TimeSpan MinFight = TimeSpan.FromSeconds(15);    // shorter boss "fights" are discarded
        private static readonly TimeSpan PvpEndGap = TimeSpan.FromSeconds(15);   // no PvP hits this long = PvP over
        private static readonly TimeSpan MinPvp = TimeSpan.FromSeconds(5);       // a stray hit or two isn't worth a file
        private static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(60); // after ffmpeg crashed

        // Quality presets: (fps, constant-quality level, max bitrate in Mbit/s).
        private static readonly (int Fps, int Cq, int MaxMbps)[] Presets =
        {
            (60, 28, 40), // High
            (60, 32, 25), // Balanced
            (30, 36, 12), // Small
        };

        private readonly CombatSessionManager sessions;
        private readonly IAppSettingsService settings;
        private readonly ILogger<BossRecorder> logger;
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

        private Process? ffmpeg;
        private AudioFeed? audio;
        private string? tempFile;
        private DateTime recordingStarted;
        private DateTime lastBossHit;
        private DateTime? killedAt;
        private DateTime lastPvpHit = DateTime.MinValue;
        private DateTime startBlockedUntil = DateTime.MinValue;
        private bool hadBoss;
        private string bossName = "Boss";
        private string? ffmpegPath;

        /// <summary>Raised on the UI thread when recording starts or stops.</summary>
        public event Action? StateChanged;

        public bool IsRecording => ffmpeg is not null || bufferFight;
        public TimeSpan Elapsed => IsRecording ? DateTime.Now - recordingStarted : TimeSpan.Zero;
        public string? LastError { get; private set; }

        public BossRecorder(CombatSessionManager sessions, IAppSettingsService settings, ILogger<BossRecorder> logger)
        {
            this.sessions = sessions;
            this.settings = settings;
            this.logger = logger;
            timer.Tick += (_, _) => Tick();
        }

        public void Start()
        {
            timer.Start();
            StartHousekeeping();
        }

        /// <summary>App is closing: stop and finish the current file (blocks for a few seconds at most).</summary>
        public void Shutdown()
        {
            timer.Stop();
            if (IsRecording) StopRecording(DateTime.Now, wait: true);
            StopBuffer(wait: true);
        }

        /// <summary>A recording started with the Start button: runs until Stop (or the 30 min cap), not until the fight ends.</summary>
        private bool manual;

        private static readonly TimeSpan MaxManualRecording = TimeSpan.FromHours(2);

        /// <summary>Start / Stop button and hotkey: starts a manual recording, or stops whatever is recording (a sound confirms it).</summary>
        public void ToggleManual()
        {
            var now = DateTime.Now;
            if (IsRecording)
            {
                StopRecording(now, wait: false);
                System.Media.SystemSounds.Exclamation.Play();
                return;
            }
            startBlockedUntil = DateTime.MinValue; // pressing Start is an explicit retry
            StartRecording("Manual", now, boss: false);
            manual = IsRecording;
            if (manual) System.Media.SystemSounds.Asterisk.Play();
            else System.Media.SystemSounds.Hand.Play();
        }

        private void Tick()
        {
            var now = DateTime.Now;
            var fight = settings.RecorderEnabled ? sessions.GetActiveBossFight() : null;
            AionDpsMeter.Services.Services.Session.PvpFightInfo? pvp = null; // PvP recording was removed (cleanup); boss fights only
            // Only your own boss fights: someone else fighting a boss near you doesn't start a recording.
            bool bossOn = fight is { UserLastHitAt: { } myHit } && now - myHit <= StartWindow && !(fight.HpTotal > 0 && fight.HpCurrent <= 0);
            bool pvpOn = pvp is not null && now - pvp.LastHitAt <= StartWindow;

            // ffmpeg died on its own (encoder busy, monitor gone...): don't keep showing REC for a dead
            // process, keep what was written, and don't relaunch it every second.
            TickBuffer(now);

            if (ffmpeg is { HasExited: true })
            {
                int code = ffmpeg.ExitCode;
                StopRecording(now, wait: false);
                startBlockedUntil = now + StartRetryDelay;
                Fail($"ffmpeg stopped unexpectedly (exit code {code}), see log; retrying in {StartRetryDelay.TotalSeconds:F0}s");
                return;
            }

            // Safety net: never record longer than MaxRecording in one go.
            if (IsRecording && now - recordingStarted > (manual ? MaxManualRecording : MaxRecording))
            {
                logger.LogWarning("Recording reached {Max} minutes; stopping it", MaxRecording.TotalMinutes);
                StopRecording(now, wait: false);
                return;
            }

            if (!IsRecording)
            {
                if (now < startBlockedUntil) return;
                if (bossOn) StartRecording(fight!.Name, fight.LastHitAt, boss: true);
                else if (pvpOn) StartRecording(PvpLabel(pvp!), pvp!.LastHitAt, boss: false);
                return;
            }

            if (fight is not null && fight.LastHitAt >= recordingStarted - StartWindow)
            {
                if (fight.LastHitAt > lastBossHit) lastBossHit = fight.LastHitAt;
                if (!hadBoss) { hadBoss = true; bossName = fight.Name; } // a boss outranks PvP in the file name
                if (fight.HpCurrent <= 0 && fight.HpTotal > 0) killedAt ??= now;
            }
            if (pvp is not null && pvp.LastHitAt > lastPvpHit)
            {
                lastPvpHit = pvp.LastHitAt;
                if (!hadBoss) bossName = PvpLabel(pvp);
            }

            if (manual) return;

            bool bossOver = !hadBoss || (killedAt is { } k ? now - k >= Tail : (fight is null || now - lastBossHit > EndGap));
            bool pvpOver = now - lastPvpHit > PvpEndGap;
            if (bossOver && pvpOver)
            {
                var lastAction = lastBossHit > lastPvpHit ? lastBossHit : lastPvpHit;
                var end = killedAt is { } kill && kill > lastPvpHit ? kill : lastAction;
                StopRecording(end + Tail, wait: false);
            }
        }

        private static string PvpLabel(PvpFightInfo pvp) =>
            pvp.OpponentCount > 1 ? $"PvP vs {pvp.Opponent} +{pvp.OpponentCount - 1}" : $"PvP vs {pvp.Opponent}";

        // ------------------------------------------------------------------ start / stop

        private void StartRecording(string label, DateTime lastHit, bool boss)
        {
            // Pre-roll: the rolling buffer is already recording; just mark where the fight starts.
            if (buffer is not null)
            {
                BeginBufferedFight(label, lastHit, boss);
                return;
            }

            ffmpegPath ??= FindFfmpeg();
            if (ffmpegPath is null)
            {
                Fail("ffmpeg not found: run  winget install Gyan.FFmpeg  or put ffmpeg.exe next to the meter");
                return;
            }

            try
            {
                Directory.CreateDirectory(settings.RecorderFolder);
                tempFile = Path.Combine(settings.RecorderFolder, $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.mkv");
                ffmpeg = LaunchCapture($"\"{tempFile}\"", out audio);
                BeginFight(label, lastHit, boss);
                logger.LogInformation("Recording started: {Label} -> {File}", label, tempFile);
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Fail("Could not start recording: " + ex.Message);
            }
        }

        private void BeginFight(string label, DateTime lastHit, bool boss)
        {
            recordingStarted = DateTime.Now;
            hadBoss = boss;
            lastBossHit = boss ? lastHit : DateTime.MinValue;
            lastPvpHit = boss ? DateTime.MinValue : lastHit;
            killedAt = null;
            bossName = label;
            LastError = null;
        }

        /// <summary>
        /// Starts ffmpeg capturing the screen (and game audio) into <paramref name="output"/> (output options + path).
        /// Shared by normal recordings and the pre-roll buffer, so both produce identical streams.
        /// </summary>
        private Process LaunchCapture(string output, out AudioFeed? feed)
        {
            var (fps, cq, maxMbps) = Presets[Math.Clamp(settings.RecorderQuality, 0, Presets.Length - 1)];
            feed = null;
            if (settings.RecorderAudio)
            {
                try { feed = new AudioFeed($"aion2rec-{Environment.ProcessId}-{DateTime.Now.Ticks}"); }
                catch (Exception ex) { logger.LogWarning(ex, "Audio capture unavailable, recording video only"); }
            }

            try
            {
                var args = new StringBuilder("-hide_banner -loglevel warning -y ");
                args.Append($"-f lavfi -i \"ddagrab=output_idx={Math.Max(0, settings.RecorderMonitor)}:framerate={fps}:draw_mouse=0\" ");
                if (feed is not null)
                    args.Append($"-thread_queue_size 4096 -f {feed.FfmpegFormat} -ar {feed.SampleRate} -ac {feed.Channels} -i \"{feed.PipePath}\" ");
                args.Append($"-map 0:v -c:v av1_nvenc -preset p5 -tune hq -rc vbr -cq {cq} -b:v 0 -maxrate {maxMbps}M -bufsize {maxMbps * 2}M -spatial-aq 1 -g {fps * 2} ");
                if (feed is not null) args.Append("-map 1:a -c:a aac -b:a 160k -ac 2 ");
                args.Append(output);

                var psi = new ProcessStartInfo(ffmpegPath!, args.ToString())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,  // "q" = stop cleanly
                    RedirectStandardError = true,
                };
                var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
                ChildProcessJob.Add(process); // Windows ends ffmpeg if the meter dies
                // Below normal so the game always wins; the heavy work is on the GPU anyway.
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* already exited */ }
                var errors = new StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null && errors.Length < 4000) errors.AppendLine(e.Data); };
                process.BeginErrorReadLine();
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    if (process.ExitCode != 0 && errors.Length > 0)
                        logger.LogWarning("ffmpeg exited with {Code}: {Errors}", process.ExitCode, errors.ToString().Trim());
                };

                feed?.Start(); // waits for ffmpeg to open the pipe on a background thread
                return process;
            }
            catch
            {
                feed?.Dispose();
                feed = null;
                throw;
            }
        }

        private void StopRecording(DateTime endAt, bool wait)
        {
            if (manual) lastPvpHit = endAt; // the whole manual recording counts, so it isn't discarded as too short
            manual = false;
            if (bufferFight)
            {
                EndBufferedFight(endAt, wait);
                return;
            }
            var process = ffmpeg!;
            var feed = audio;
            var file = tempFile!;
            var started = recordingStarted;
            var boss = bossName;
            var killed = killedAt is not null;
            var lastAction = lastBossHit > lastPvpHit ? lastBossHit : lastPvpHit;
            var fightLength = lastAction - started;
            var minLength = hadBoss ? MinFight : MinPvp;
            ffmpeg = null;
            audio = null;
            StateChanged?.Invoke();

            var finish = Task.Run(() =>
            {
                try
                {
                    try { process.StandardInput.Write('q'); process.StandardInput.Flush(); } catch { /* already gone */ }
                    if (!process.WaitForExit(10_000)) process.Kill();
                    feed?.Dispose();

                    if (fightLength < minLength)
                    {
                        File.Delete(file);
                        logger.LogInformation("Recording discarded (fight shorter than {Min}s)", minLength.TotalSeconds);
                        return;
                    }
                    Finalize(file, started, endAt, boss, killed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Finishing boss recording failed; raw file kept at {File}", file);
                }
            });
            if (wait) finish.Wait(TimeSpan.FromSeconds(20));
        }

        /// <summary>Trim to the end of the fight and remux .mkv to .mp4 (stream copy, takes seconds).</summary>
        private void Finalize(string mkv, DateTime started, DateTime endAt, string boss, bool killed)
        {
            var length = endAt - started;
            if (length <= TimeSpan.Zero) length = TimeSpan.FromSeconds(1);
            string mp4 = Path.Combine(Path.GetDirectoryName(mkv)!, RecordingName(started, length, boss, killed));

            var psi = new ProcessStartInfo(ffmpegPath!,
                $"-hide_banner -loglevel error -y -i \"{mkv}\" -t {length.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} -c copy -movflags +faststart \"{mp4}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var remux = Process.Start(psi)!;
            string err = remux.StandardError.ReadToEnd();
            remux.WaitForExit();
            if (remux.ExitCode == 0 && File.Exists(mp4))
            {
                File.Delete(mkv);
                logger.LogInformation("Boss recording saved: {File}", mp4);
                RunHousekeeping(recoverLeftovers: false);
            }
            else
            {
                logger.LogWarning("Remux failed ({Code}): {Error}. Raw file kept: {File}", remux.ExitCode, err.Trim(), mkv);
            }
        }

        /// <summary>"2026-10-08 15-29-02 Boss (kill, 0m47s).mp4" (housekeeping only ever deletes names like this).</summary>
        private static string RecordingName(DateTime started, TimeSpan length, string boss, bool killed)
        {
            string safeBoss = string.Concat(boss.Split(Path.GetInvalidFileNameChars())).Trim();
            if (safeBoss.Length == 0) safeBoss = "Boss";
            return $"{started:yyyy-MM-dd HH-mm-ss} {safeBoss} ({(killed ? "kill" : "end")}, {(int)length.TotalMinutes}m{length.Seconds:00}s).mp4";
        }

        private void Fail(string message)
        {
            if (LastError != message) logger.LogWarning("Boss recorder: {Message}", message);
            LastError = message;
            StateChanged?.Invoke();
        }

        /// <summary>PATH first, then winget's alias and package folders (PATH changes need a re-login).</summary>
        private static string? FindFfmpeg()
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");   // dropped next to the meter
            if (File.Exists(beside)) return beside;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { /* bad PATH entry */ }
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var link = Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            if (File.Exists(link)) return link;
            var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
            {
                return Directory.EnumerateDirectories(packages, "Gyan.FFmpeg*")
                    .SelectMany(d => Directory.EnumerateFiles(d, "ffmpeg.exe", SearchOption.AllDirectories))
                    .FirstOrDefault();
            }
            return null;
        }

        /// <summary>
        /// Loopback audio → named pipe → ffmpeg. WASAPI loopback sends nothing while the PC is
        /// silent, so silence is filled in to keep the audio in step with the video.
        /// </summary>
        private sealed class AudioFeed : IDisposable
        {
            private readonly WasapiLoopbackCapture capture;
            private readonly NamedPipeServerStream pipe;
            private readonly Lock gate = new();
            private readonly Stopwatch clock = new();
            private Timer? silenceTimer;
            private long bytesWritten;
            private volatile bool connected;
            private bool disposed;

            public string PipePath { get; }
            public int SampleRate => capture.WaveFormat.SampleRate;
            public int Channels => capture.WaveFormat.Channels;
            private int BytesPerSecond => capture.WaveFormat.AverageBytesPerSecond;
            private int BlockAlign => capture.WaveFormat.BlockAlign;

            /// <summary>ffmpeg raw input format matching the device mix format.</summary>
            public string FfmpegFormat => capture.WaveFormat.BitsPerSample switch
            {
                16 => "s16le",
                24 => "s24le",
                32 when capture.WaveFormat.Encoding == WaveFormatEncoding.Pcm => "s32le",
                _ => "f32le", // IEEE float or WAVE_FORMAT_EXTENSIBLE float (the usual mix format)
            };

            public AudioFeed(string pipeName)
            {
                PipePath = $@"\\.\pipe\{pipeName}";
                capture = new WasapiLoopbackCapture();
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 1 << 20);
                capture.DataAvailable += (_, e) => Write(e.Buffer, e.BytesRecorded);
            }

            public void Start()
            {
                Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await pipe.WaitForConnectionAsync(cts.Token);
                        lock (gate)
                        {
                            if (disposed) return;
                            connected = true;
                            clock.Start();
                        }
                        capture.StartRecording();
                        silenceTimer = new Timer(_ => FillSilence(), null, 20, 20);
                    }
                    catch { /* ffmpeg never opened the pipe (it failed to start); nothing to feed */ }
                });
            }

            private void Write(byte[] buffer, int count)
            {
                lock (gate)
                {
                    if (!connected || disposed || count <= 0) return;
                    try
                    {
                        pipe.Write(buffer, 0, count);
                        bytesWritten += count;
                    }
                    catch (IOException) { disposed = true; } // ffmpeg closed the pipe
                }
            }

            private void FillSilence()
            {
                lock (gate)
                {
                    if (!connected || disposed) return;
                    long expected = (long)(clock.Elapsed.TotalSeconds * BytesPerSecond);
                    long behind = expected - bytesWritten;
                    if (behind < BytesPerSecond / 25) return; // within 40 ms: real audio is flowing
                    behind -= behind % BlockAlign;
                    var zeros = new byte[(int)Math.Min(behind, BytesPerSecond)];
                    try
                    {
                        pipe.Write(zeros, 0, zeros.Length);
                        bytesWritten += zeros.Length;
                    }
                    catch (IOException) { disposed = true; }
                }
            }

            public void Dispose()
            {
                lock (gate) { disposed = true; }
                silenceTimer?.Dispose();
                try { capture.StopRecording(); } catch { /* not started */ }
                capture.Dispose();
                pipe.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;

namespace PortForge
{
    public sealed class GraphicsProfile
    {
        public string FpsLimit = "game";
        public decimal Resolution = 1;
        public int Anisotropy = 0;
        public int Upscaler = 1;
        public bool PixelSmoothing;
        public bool Shadows = true, VSync = true, Docked = true, Fullscreen;

        public GraphicsProfile Copy() { return (GraphicsProfile)MemberwiseClone(); }
        public void Validate()
        {
            int fps;
            if (FpsLimit != "game" && FpsLimit != "0" && (!Int32.TryParse(FpsLimit, NumberStyles.None, CultureInfo.InvariantCulture, out fps) || fps < 10 || fps > 1000))
                throw new ArgumentException("FPS must be Game speed, Unlimited, or a whole number from 10 to 1000.");
            if (Resolution < .25m || Resolution > 4m) throw new ArgumentException("Render scale must be between 25% and 400%.");
            if (Anisotropy < 0 || Anisotropy > 16) throw new ArgumentException("Texture filtering must be between 0 and 16.");
            if (Upscaler < 0 || Upscaler > 1) throw new ArgumentException("Choose Linear or AMD FSR 1 upscaling.");
        }
        public Dictionary<string, string> Values()
        {
            Validate();
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                {"fps_limit", FpsLimit}, {"resolution", Resolution.ToString(CultureInfo.InvariantCulture)},
                {"anisotropy", Anisotropy.ToString(CultureInfo.InvariantCulture)}, {"upscaler", Upscaler.ToString(CultureInfo.InvariantCulture)}, {"shadows", Shadows ? "1" : "0"},
                {"pixel_smoothing", PixelSmoothing ? "1" : "0"}, {"vsync", VSync ? "1" : "0"}, {"docked", Docked ? "1" : "0"}, {"fullscreen", Fullscreen ? "1" : "0"} };
        }
        public static GraphicsProfile Preset(string name, GraphicsProfile previous, bool protectGraphics = false)
        {
            var p = previous.Copy();
            // Keep timing, fullscreen and VSync. Protected engines use the game's own rendering modes.
            p.Shadows = true;
            if (name == "Performance") { p.Resolution = .75m; p.Anisotropy = 2; }
            else if (name == "Balanced") { p.Resolution = 1; p.Anisotropy = 8; }
            else if (name == "Quality") { p.Resolution = 1.5m; p.Anisotropy = 16; }
            else throw new ArgumentException("Choose Performance, Balanced or Quality.");
            if (protectGraphics)
            {
                p.Resolution = 1;
                p.Docked = name != "Performance";
                if (name == "Performance") p.Upscaler = 1;
            }
            return p;
        }
    }

    public sealed class RuntimeCapabilities
    {
        public readonly HashSet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool SupportsGraphics { get { return new[] {"fps_limit", "resolution", "anisotropy", "shadows", "vsync", "docked", "fullscreen"}.All(Keys.Contains); } }
        public bool ProtectsRenderScale { get { return Keys.Contains("render_scale_safety"); } }
        public static RuntimeCapabilities Parse(string table)
        {
            var result = new RuntimeCapabilities();
            foreach (string row in table.Split('\n')) { var cells = row.TrimEnd('\r').Split('\t'); if (cells.Length >= 8) result.Keys.Add(cells[1]); }
            if (!result.SupportsGraphics) throw new InvalidOperationException("This game engine does not expose the required graphics controls. Use a supported ScarletNX build.");
            return result;
        }
        public static RuntimeCapabilities Read(string exe, CancellationToken token)
        {
            GameOptimization.ValidateGame(exe);
            return Parse(GameOptimization.Run(exe, "--settings", 15, token));
        }
    }

    public sealed class BenchmarkSample
    {
        public string Name, Diagnostic;
        public GraphicsProfile Profile;
        public int Frames, SlowFrames, WorstFrameMs;
        public decimal P99FrameMs;
        public bool HasFramePercentile;
        public double Fps;
        public bool Success;
    }

    public sealed class OptimizationResult
    {
        public GraphicsProfile Profile;
        public string Summary, ReportPath;
        public readonly List<BenchmarkSample> Samples = new List<BenchmarkSample>();
    }

    public static class GameOptimization
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly object processGate = new object();
        private static readonly HashSet<int> probes = new HashSet<int>();
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint StillActive = 259;
        private const string ProcessCheckFailure = "Windows could not check a process with this game's name. Close that process or retry Play.";
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        private static string RunningProcessImagePath(int processId)
        {
            // MainModule enumerates modules and can fail inside .NET even for our own live game.
            // This API needs only limited query access and works across 32/64-bit processes.
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 87) return null; // ERROR_INVALID_PARAMETER: this PID has already exited.
                throw new IOException(ProcessCheckFailure, new System.ComponentModel.Win32Exception(error));
            }
            try
            {
                uint exitCode;
                if (!GetExitCodeProcess(handle, out exitCode))
                    throw new IOException(ProcessCheckFailure, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                if (exitCode != StillActive) return null;
                var path = new StringBuilder(32768); // Maximum Windows Unicode path, including the terminator.
                int size = path.Capacity;
                if (!QueryFullProcessImageName(handle, 0, path, ref size))
                {
                    int error = Marshal.GetLastWin32Error();
                    // An exit between the status check and query is harmless; every other failure stays closed.
                    if (GetExitCodeProcess(handle, out exitCode) && exitCode != StillActive) return null;
                    throw new IOException(ProcessCheckFailure, new System.ComponentModel.Win32Exception(error));
                }
                if (size == 0) throw new IOException(ProcessCheckFailure);
                return path.ToString();
            }
            finally { CloseHandle(handle); }
        }
        // A --settings query has the game's filename too, but does not own its save or run the game.
        public static Process FindRunningGame(string exe)
        {
            string full = Path.GetFullPath(exe);
            lock (processGate)
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
            {
                bool keep = false;
                try {
                    if (probes.Contains(process.Id)) continue;
                    keep = String.Equals(RunningProcessImagePath(process.Id), full, StringComparison.OrdinalIgnoreCase);
                    if (keep) return process;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) {
                    throw new IOException(ProcessCheckFailure);
                }
                finally { if (!keep) process.Dispose(); }
            }
            return null;
        }
        public static bool BringGameForward(string exe)
        {
            using (var process = FindRunningGame(exe))
            {
                if (process == null) return false;
                try { process.Refresh(); if (process.MainWindowHandle != IntPtr.Zero) { ShowWindowAsync(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); } }
                catch (InvalidOperationException) { return false; }
                return true;
            }
        }
        public static Process StartForPlayer(string exe)
        {
            ValidateGame(exe);
            lock (processGate)
            {
                var existing = FindRunningGame(exe);
                if (existing != null) { BringGameForward(exe); return existing; }
                return Process.Start(new ProcessStartInfo(Path.GetFullPath(exe)) { WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe)) });
            }
        }
        public static string ConfigPath(string exe) { return Path.ChangeExtension(Path.GetFullPath(exe), ".ini"); }
        public static void ValidateGame(string exe)
        {
            if (String.IsNullOrWhiteSpace(exe) || !File.Exists(exe) || !String.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose the generated game's .exe file.");
            string module = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_code.dll");
            if (!File.Exists(module)) throw new ArgumentException("Choose a PortForge game executable with its _code.dll beside it.");
            NativeConversion.RequireNativeX64(exe);
            NativeConversion.RequireNativeX64(module);
        }
        public static void EnsureStopped(string exe)
        {
            using (var process = FindRunningGame(exe))
                if (process != null) throw new IOException("Close the game before changing its settings or running a tuning test. Use Play game to return to its window.");
        }
        public static GraphicsProfile Load(string exe)
        {
            var p = new GraphicsProfile();
            string path = ConfigPath(exe);
            if (!File.Exists(path)) return p;
            var values = ReadValues(File.ReadAllText(path));
            string v; decimal scale; int n;
            if (values.TryGetValue("fps_limit", out v)) p.FpsLimit = v;
            if (values.TryGetValue("resolution", out v) && Decimal.TryParse(v, NumberStyles.Number, Invariant, out scale)) p.Resolution = scale;
            if (values.TryGetValue("anisotropy", out v) && Int32.TryParse(v, out n)) p.Anisotropy = n;
            if (values.TryGetValue("upscaler", out v) && Int32.TryParse(v, out n)) p.Upscaler = n;
            if (values.TryGetValue("pixel_smoothing", out v)) p.PixelSmoothing = v == "1";
            if (values.TryGetValue("shadows", out v)) p.Shadows = v != "0";
            if (values.TryGetValue("vsync", out v)) p.VSync = v != "0";
            if (values.TryGetValue("docked", out v)) p.Docked = v != "0";
            if (values.TryGetValue("fullscreen", out v)) p.Fullscreen = v != "0";
            p.Validate(); return p;
        }
        public static Dictionary<string, string> ReadValues(string ini)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string row in ini.Split('\n'))
            {
                Match m = Regex.Match(row, @"^\s*([A-Za-z_][A-Za-z_0-9]*)\s*=\s*([^;#\r\n]*)");
                if (m.Success) values[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
            return values;
        }
        public static string Merge(string ini, IDictionary<string, string> settings)
        {
            var remaining = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            var lines = new List<string>();
            foreach (string row in ini.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
            {
                var m = Regex.Match(row, @"^(\s*)([A-Za-z_][A-Za-z_0-9]*)(\s*=\s*)([^;#]*)(.*)$");
                string value;
                if (m.Success && settings.TryGetValue(m.Groups[2].Value, out value))
                {
                    // Replace every duplicate too; the runtime takes the last occurrence.
                    lines.Add(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + value + (m.Groups[5].Value.Length > 0 ? " " + m.Groups[5].Value : ""));
                    remaining.Remove(m.Groups[2].Value);
                }
                else lines.Add(row);
            }
            if (remaining.Count > 0)
            {
                lines.Add(""); lines.Add("[PortForge]"); lines.Add("; Per-game graphics and performance settings. Other settings are preserved.");
                foreach (var pair in remaining) lines.Add(pair.Key + "=" + pair.Value);
            }
            return String.Join("\r\n", lines) + "\r\n";
        }
        public static void AtomicWrite(string path, byte[] bytes, string backup)
        {
            string temp = path + ".portforge-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, bytes); if (File.Exists(path)) ConversionFiles.ReplaceWithRetry(temp, path, CancellationToken.None, backup); else File.Move(temp, path); }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        public static string Save(string exe, GraphicsProfile profile, RuntimeCapabilities capabilities)
        {
            ValidateGame(exe); profile.Validate(); EnsureStopped(exe);
            if (capabilities == null || !capabilities.SupportsGraphics) throw new InvalidOperationException("Check this game's engine before saving graphics settings.");
            string path = ConfigPath(exe);
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            var existing=ReadValues(text);
            var supported = profile.Values();
            if (capabilities.ProtectsRenderScale) { supported["resolution"] = "1"; supported["render_scale_safety"] = "1"; }
            if (!capabilities.Keys.Contains("upscaler")) supported.Remove("upscaler");
            if (!capabilities.Keys.Contains("pixel_smoothing")) supported.Remove("pixel_smoothing"); // an older engine still starts the game
            ControllerSettings.AddTo(supported, capabilities, existing); // the Controllers window's choice, for every game
            if(supported.All(item=>existing.ContainsKey(item.Key)&&existing[item.Key]==item.Value))return null;
            string backup = File.Exists(path) ? path + ".before-portforge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak" : null;
            AtomicWrite(path, new UTF8Encoding(false).GetBytes(Merge(text, supported)), backup);
            return backup;
        }
        public static void SaveAndVerify(string exe, GraphicsProfile profile, RuntimeCapabilities capabilities)
        {
            Save(exe,profile,capabilities);
            var actual=Load(exe).Values();
            foreach(var item in profile.Values()) if(capabilities.Keys.Contains(item.Key)&&actual[item.Key]!=(item.Key=="resolution"&&capabilities.ProtectsRenderScale?"1":item.Value)) throw new IOException("The saved "+item.Key+" setting did not match. The game was not started.");
            if(capabilities.ProtectsRenderScale) {
                string enabled;
                if(!ReadValues(File.ReadAllText(ConfigPath(exe))).TryGetValue("render_scale_safety",out enabled)||enabled!="1")
                    throw new IOException("Graphics protection could not be verified. The game was not started.");
            }
        }
        public static Process SaveAndStart(string exe, GraphicsProfile profile, RuntimeCapabilities capabilities)
        {
            var existing = FindRunningGame(exe);
            if (existing != null) { BringGameForward(exe); return existing; }
            SaveAndVerify(exe,profile,capabilities);
            return StartForPlayer(exe);
        }
        public static void Initialize(string exe)
        {
            // Engine defaults already auto-size pipeline and JIT workers to the local CPU.
            // Keep native timing and resolution; do not claim an untested hardware preset was benchmarked.
            var cap = RuntimeCapabilities.Read(exe, CancellationToken.None);
            if (!File.Exists(ConfigPath(exe))) Save(exe, new GraphicsProfile(), cap);
        }
        /// <summary>Plays the opening of the game once, hidden, so shaders and pipelines are built and cached before the player gets there. Settings, save and log are put back afterwards.</summary>
        public static string WarmCaches(string exe, IProgress<string> progress, CancellationToken token)
        {
            ValidateGame(exe); EnsureStopped(exe);
            var cap = RuntimeCapabilities.Read(exe, token);
            if (!cap.Keys.Contains("log")) throw new IOException("This engine cannot run a timed warm-up.");
            string config = ConfigPath(exe);
            byte[] original = File.Exists(config) ? File.ReadAllBytes(config) : null;
            using (new GameStateGuard(exe))
            {
                try
                {
                    if (progress != null) progress.Report("Warming the shader cache by playing the opening of the game (" + GameplaySeconds + " seconds). Your save is backed up and restored.");
                    string output = Run(exe, "-H --seconds=" + GameplaySeconds + " --log=1 --fullscreen=0 --vsync=0 --fps_limit=0 --script=" + GameplayScript + " --press_len=300", GameplaySeconds + 45, token);
                    var last = Regex.Matches(output, @"(\d+) pipelines made \((\d+) from the cache, (\d+) ms making, (\d+) ms waited for\)").Cast<Match>().LastOrDefault();
                    return "Shader cache warmed by a " + GameplaySeconds + "-second play-through" + (last == null ? "." : ": " + last.Groups[1].Value + " pipelines built this run, " + last.Groups[2].Value + " already cached, " + last.Groups[4].Value + " ms waited for.");
                }
                finally
                {
                    EnsureStopped(exe);
                    if (original != null) AtomicWrite(config, original, null); else if (File.Exists(config)) File.Delete(config);
                }
            }
        }
        public static string Run(string exe, string arguments, int timeoutSeconds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            exe = Path.GetFullPath(exe);
            var info = new ProcessStartInfo(exe, arguments) { WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe)), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            // a timed run plays the game, so Play can stop it; a runtime run for another game's folder (--game=) is that game's test
            var owner = Regex.Match(arguments, "--game=(\"[^\"]+\"|\\S+)");
            string tested = owner.Success ? owner.Groups[1].Value.Trim('"') : exe;
            bool gameplay = Regex.IsMatch(arguments, @"(^|\s)--seconds=");
            if (gameplay && PlayFirst.Holding(tested)) throw new OperationCanceledException("The player started this game. PortForge's test will run another time.");
            bool probe = Regex.IsMatch(arguments, @"(^|\s)--(?:settings|version|help)(?:\s|$)");
            Process started;
            lock (processGate) { started = Process.Start(info); if (probe) probes.Add(started.Id); }
            using (var process = started)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                var watch = Stopwatch.StartNew();
                object played = gameplay ? PlayFirst.Track(tested, process) : null;
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        token.ThrowIfCancellationRequested();
                        if (watch.Elapsed.TotalSeconds > timeoutSeconds) throw new TimeoutException("The game did not finish its test in time. No tuning result was applied.");
                    }
                    if (played != null && PlayFirst.Release(played)) throw new OperationCanceledException("Stopped so the player can play. The test will run another time.");
                    token.ThrowIfCancellationRequested();
                    string text = stdout.Result + "\n" + stderr.Result;
                    if (process.ExitCode != 0) throw new IOException("The game engine stopped (exit " + process.ExitCode + "). " + text.Substring(Math.Max(0, text.Length - 1800)));
                    var duration = Regex.Match(arguments, @"--seconds=(\d+)");
                    if (duration.Success && watch.Elapsed.TotalSeconds < Int32.Parse(duration.Groups[1].Value) - 1)
                        throw new IOException("The game closed before its timed test finished. No performance recommendation was applied.");
                    return text;
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                    if (probe) lock (processGate) probes.Remove(process.Id);
                    if (played != null) PlayFirst.Release(played);
                }
            }
        }
        public static BenchmarkSample ParseSample(string name, GraphicsProfile profile, string output, int seconds)
        {
            var m = Regex.Match(output, @"(?m)^frames=(\d+)\b");
            int frames;
            if (!m.Success || !Int32.TryParse(m.Groups[1].Value, out frames) || frames < 2 || output.Contains("is not a setting") || Regex.IsMatch(output, @"\bFATAL:|\*\*\* Crash|No new frame for \d+ seconds"))
                throw new IOException("The engine did not report usable frame measurements. Settings were left unchanged.");
            var slow = Regex.Matches(output, @"(\d+) frames over 50 ms").Cast<Match>().Select(x => Int32.Parse(x.Groups[1].Value)).ToList();
            var stalls = GameDiagnostics.StallFrames(output);
            var worst = Regex.Matches(output, @"(?:the slowest|worst) (\d+) ms").Cast<Match>().Select(x => Int32.Parse(x.Groups[1].Value)).Concat(stalls.Select(f => f.FrameMs)).ToList();
            var percentiles = Regex.Matches(output, @"frame-time p99 ([0-9]+(?:\.[0-9]+)?) ms").Cast<Match>()
                .Select(x => Decimal.Parse(x.Groups[1].Value, NumberStyles.AllowDecimalPoint, Invariant)).Where(x => x > 0).ToList();
            return new BenchmarkSample { Name = name, Profile = profile, Frames = frames, Fps = (double)frames / seconds, SlowFrames = Math.Max(slow.Count == 0 ? 0 : slow.Max(), stalls.Count), WorstFrameMs = worst.Count == 0 ? 0 : worst.Max(), P99FrameMs = percentiles.Count == 0 ? 0 : percentiles.Max(), HasFramePercentile = percentiles.Count > 0, Success = true, Diagnostic = output };
        }
        public static BenchmarkSample Select(IEnumerable<BenchmarkSample> samples, int target, decimal maxResolution = 4)
        {
            var good = samples.Where(x => x.Success && x.Frames > 1 && x.Profile.Resolution <= maxResolution).ToList();
            if (good.Count == 0) throw new IOException("No profile completed a usable test. Your settings were left unchanged.");
            double fastest = good.Max(x => x.Fps), threshold = Math.Min(target * .95, fastest * .97);
            // At a game's own lower ceiling, retain quality when a reduced resolution brings no measured benefit.
            var fast = good.Where(x => x.Fps >= threshold).ToList();
            return PreferStableFrameTimes(fast);
        }
        public static int GameplaySeconds = 45;
        public static readonly string GameplayScript = String.Join(",", Enumerable.Range(0, 9).Select(i => (6000 + i * 4000) + ":1"));
        public static BenchmarkSample SelectSmooth(IEnumerable<BenchmarkSample> samples, int target, decimal maxResolution)
        {
            var good = samples.Where(x => x.Success && x.Frames > 1 && x.Profile.Resolution <= maxResolution).ToList();
            if (good.Count == 0) throw new IOException("No profile completed a usable test. Your settings were left unchanged.");
            double threshold = Math.Min(target * .95, good.Max(x => x.Fps) * .97);
            var fast = good.Where(x => x.Fps >= threshold).ToList();
            int slowBudget = fast.Min(x => x.SlowFrames) + 1;
            var smooth = fast.Where(x => x.SlowFrames <= slowBudget).ToList();
            double worstBudget = Math.Max(smooth.Min(x => x.WorstFrameMs) * 1.5, smooth.Min(x => x.WorstFrameMs) + 100);
            return PreferStableFrameTimes(smooth.Where(x => x.WorstFrameMs <= worstBudget).ToList());
        }
        private static BenchmarkSample PreferStableFrameTimes(List<BenchmarkSample> samples)
        {
            if (samples.All(x => x.HasFramePercentile))
            {
                decimal best = samples.Min(x => x.P99FrameMs);
                // Treat sub-half-millisecond differences as measurement noise; prefer image quality within that band.
                samples = samples.Where(x => x.P99FrameMs <= best + .5m).ToList();
            }
            return samples.OrderByDescending(x => x.Profile.Resolution).ThenByDescending(x => x.Profile.Docked).ThenByDescending(x => x.Profile.Anisotropy).ThenBy(x => x.SlowFrames).First();
        }
        private static string FrameTimeSummary(BenchmarkSample sample)
        {
            return sample.HasFramePercentile
                ? ", p99 " + sample.P99FrameMs.ToString("F3", Invariant) + " ms (about " + (1000.0 / (double)sample.P99FrameMs).ToString("F1", Invariant) + " FPS 1% low)"
                : ", 99th-percentile frame time not reported by this engine";
        }

        private static string RunProtectedSample(string exe, string args, int timeout, CancellationToken token, bool gameplay)
        {
            using (var guard = gameplay ? new GameStateGuard(exe) : null)
                return Run(exe, args, timeout, token);
        }

        public static OptimizationResult Tune(string exe, GraphicsProfile desired, int target, IProgress<string> progress, CancellationToken token, bool gameplay = true, bool preferSmoothness = false)
        {
            ValidateGame(exe); EnsureStopped(exe); desired.Validate();
            if (target < 10 || target > 1000) throw new ArgumentException("Choose a test target between 10 and 1000 FPS.");
            var cap = RuntimeCapabilities.Read(exe, token);
            if (!cap.Keys.Contains("log")) throw new IOException("This engine cannot run a measured timed test.");
            if (cap.ProtectsRenderScale) { desired = desired.Copy(); desired.Resolution = 1; }
            string config = ConfigPath(exe);
            byte[] original = File.Exists(config) ? File.ReadAllBytes(config) : null;
            string rollback = config + ".tuning-recovery-" + Guid.NewGuid().ToString("N") + ".bak";
            if (original != null) File.WriteAllBytes(rollback, original);
            var result = new OptimizationResult();
            int seconds = gameplay ? GameplaySeconds : 20;
            string testLimit = preferSmoothness ? desired.FpsLimit : target.ToString(Invariant);
            string testVsync = preferSmoothness && desired.VSync ? "1" : "0";
            string workload = gameplay ? " --script=" + GameplayScript + " --press_len=300" : "";
            string report = Path.Combine(Path.GetDirectoryName(exe), "PortForge-performance-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".txt");
            using (var guard = gameplay ? new GameStateGuard(exe) : null)
            using (var log = new StreamWriter(report, false, new UTF8Encoding(false)) { AutoFlush = true })
            {
                log.WriteLine("PortForge measured " + (gameplay ? "gameplay" : "startup") + " tuning | " + DateTime.UtcNow.ToString("u"));
                log.WriteLine("Game: " + exe + "\r\nTarget: " + target + " FPS. " + (gameplay ? "Each sample plays the first minute of the game with scripted button presses (A) from a backed-up save. Other areas can still differ." : "Samples include loading and menus, not a gameplay guarantee."));
                log.WriteLine("Samples render offscreen. Display upscaling and presentation costs are excluded; these measurements do not establish FSR speedups or displayed 1% lows.");
                if(cap.ProtectsRenderScale)log.WriteLine("Graphics protection keeps internal scaling at 100%. Handheld and TV samples ask the game to choose its own rendering layout; some games use the same resolution in both modes. Display FSR cost is excluded.");
                try
                {
                    progress.Report(gameplay ? "Warming the shader cache by playing the opening of the game (" + seconds + " seconds). Your settings and save will be restored." : "Warming the startup scene and shader cache (8 seconds). Your original settings will be restored.");
                    log.WriteLine(RunProtectedSample(exe, "-H --seconds=" + (gameplay ? seconds : 8) + " --log=1 --fullscreen=0 --fps_limit=" + testLimit + " --vsync=" + testVsync + workload, (gameplay ? seconds : 8) + 45, token, gameplay));
                    string[] names = preferSmoothness ? (cap.ProtectsRenderScale ? new[] {"Current", "Handheld", "TV"} : new[] {"Current", "Lower", "Half"}) : new[] {"Current", "Performance", "Balanced", "Quality"};
                    var testedScales = new HashSet<string>();
                    foreach (string name in names)
                    {
                        token.ThrowIfCancellationRequested(); EnsureStopped(exe);
                        var p = name == "Current" || preferSmoothness ? desired.Copy() : GraphicsProfile.Preset(name, desired, cap.ProtectsRenderScale);
                        if (preferSmoothness)
                        {
                            if (cap.ProtectsRenderScale) { if (name != "Current") p.Docked = name == "TV"; }
                            else p.Resolution = Math.Max(.25m, desired.Resolution * (name == "Lower" ? .75m : name == "Half" ? .5m : 1m));
                        }
                        if (cap.ProtectsRenderScale) {
                            p.Resolution = 1;
                            if (!testedScales.Add("native|" + p.Anisotropy.ToString(Invariant) + "|" + p.Docked)) continue;
                        } else if (preferSmoothness && !testedScales.Add(p.Resolution.ToString(Invariant))) continue;
                        progress.Report("Testing " + name + ": " + (p.Docked ? "TV" : "handheld") + " mode, " + (p.Resolution * 100).ToString("0") + "% internal scale (" + seconds + " seconds, " + (gameplay ? "playing the opening of the game" : "startup scene") + ").");
                        // CLI overrides do not change the player's chosen limiter. Test the chosen target only for this run.
                        string args = "-H --seconds=" + seconds + " --log=1 --fullscreen=0 --fps_limit=" + testLimit + " --vsync=" + testVsync + " --resolution=" + p.Resolution.ToString(Invariant) + " --anisotropy=" + p.Anisotropy + " --shadows=" + (p.Shadows ? "1" : "0") + " --docked=" + (p.Docked ? "1" : "0") + workload;
                        try
                        {
                            var sample = ParseSample(name, p, RunProtectedSample(exe, args, seconds + 45, token, gameplay), seconds);
                            result.Samples.Add(sample); log.WriteLine("\r\n" + name + ": " + sample.Fps.ToString("F1", Invariant) + " FPS, " + sample.SlowFrames + " reported frames over 50 ms, worst " + sample.WorstFrameMs + " ms" + FrameTimeSummary(sample) + "\r\n" + sample.Diagnostic);
                            progress.Report(name + ": " + sample.Fps.ToString("F1") + " FPS in the " + (gameplay ? "gameplay" : "startup") + " sample.");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { log.WriteLine(name + " failed: " + error); progress.Report(name + " could not be measured; trying the next profile."); }
                    }
                    token.ThrowIfCancellationRequested();
                    var best = preferSmoothness ? SelectSmooth(result.Samples, target, desired.Resolution) : Select(result.Samples, target, desired.Resolution);
                    result.Profile = best.Profile.Copy();
                    result.Summary = best.Name + " chosen: " + (best.Profile.Docked ? "TV" : "handheld") + " mode, " + best.Fps.ToString("F1") + " FPS average in the " + (gameplay ? "gameplay" : "startup") + " sample at " + (best.Profile.Resolution * 100).ToString("0") + "% internal scale" + FrameTimeSummary(best) + ". " +
                        (!gameplay && best.Fps < target * .95 ? "The test did not reach the target. " : "") + (gameplay ? "Measured while playing the first scenes, including loading. Later areas can be heavier. " : "Includes loading/menus; test demanding gameplay before raising quality. ") + " Your FPS and VSync choices are preserved. Select Save settings to apply.";
                    result.ReportPath = report; log.WriteLine("\r\n" + result.Summary);
                    if (preferSmoothness) log.WriteLine("Automatic PC matching prioritized measured FPS and freezes before resolution. Scripted inputs may stay in a menu in some games; these are samples, not proof of later gameplay. Each sample starts from the original backed-up save.");
                    return result;
                }
                finally
                {
                    // The runtime rewrites its INI even during a timed run. Restore exact original bytes on every exit.
                    try
                    {
                        EnsureStopped(exe);
                        if (original != null) AtomicWrite(config, original, null);
                        else if (File.Exists(config)) File.Delete(config);
                        if (File.Exists(rollback)) File.Delete(rollback);
                    }
                    catch (Exception restoreError) { throw new IOException("Could not restore the game settings after testing. Close the game and restore the recovery copy: " + rollback, restoreError); }
                }
            }
        }
    }
}

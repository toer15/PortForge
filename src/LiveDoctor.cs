using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PortForge
{
    public sealed class DoctorEvent
    {
        public DateTime Time = DateTime.Now;
        public string Game = "", Kind = "", Title = "", Text = "";
        public bool Fixed, Important;
        public override string ToString() { return Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + Game + ": " + Title + (Text.Length > 0 ? " - " + Text : ""); }
    }

    /// <summary>What the doctor learned from one stretch of the game's log. Pure, so it can be tested without a game.</summary>
    public sealed class LogWatch
    {
        private static readonly Regex Stamp = new Regex(@"^\[\s*(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);
        private static readonly Regex Heap = new Regex(@"heap \d+ (\d+) of (\d+) MB", RegexOptions.Compiled);
        private readonly Queue<double> recentSlow = new Queue<double>();
        private readonly StringBuilder partial = new StringBuilder();
        private double lastStreakAt = -1000, lastCompileAt = -1000, lastLoadAt = -1000, lastMemoryAt = -1000;
        public int CompileFrames, LoadingFrames, UnexplainedFrames, CompileWaitMs, WorstMs;
        public bool Crashed;

        /// <summary>Feeds raw log text (any chunk size); returns the problems found in complete lines.</summary>
        public List<DoctorEvent> Feed(string text, string game)
        {
            var events = new List<DoctorEvent>();
            partial.Append(text);
            string all = partial.ToString(); int end = all.LastIndexOf('\n');
            if (end < 0) return events;
            partial.Clear(); partial.Append(all.Substring(end + 1));
            foreach (string raw in all.Substring(0, end).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                var stamp = Stamp.Match(line); double t = stamp.Success ? Double.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
                var frames = GameDiagnostics.StallFrames(line);
                if (frames.Count == 1)
                {
                    var f = frames[0]; WorstMs = Math.Max(WorstMs, f.FrameMs);
                    bool compile = f.WaitedMs >= 8 || f.Shaders > 0 && f.TranslateMs >= 8;
                    bool loading = !compile && (f.TextureKb >= 1024 || f.RenderTargets >= 20 || f.TextureUploads >= 20);
                    if (compile) { CompileFrames++; CompileWaitMs += f.WaitedMs; }
                    else if (loading) LoadingFrames++;
                    else UnexplainedFrames++;
                    if (compile && f.FrameMs >= 150 && t - lastCompileAt > 20) { lastCompileAt = t; events.Add(new DoctorEvent { Game = game, Kind = "compile", Title = "Shader stutter", Text = "a " + f.FrameMs + " ms freeze while new GPU pipelines were built. More pipeline threads and a cache warm-up are applied when you close the game." }); }
                    if (loading && f.FrameMs >= 250 && t - lastLoadAt > 20) { lastLoadAt = t; events.Add(new DoctorEvent { Game = game, Kind = "loading", Title = "Loading freeze", Text = "a " + f.FrameMs + " ms freeze while the game loaded " + (f.TextureKb / 1024) + " MB of textures. This comes from the engine, not a setting." }); }
                    if (t >= 0 && !compile && !loading && f.FrameMs >= 100)
                    {
                        recentSlow.Enqueue(t);
                        while (recentSlow.Count > 0 && t - recentSlow.Peek() > 10) recentSlow.Dequeue();
                        if (recentSlow.Count >= 8 && t - lastStreakAt > 30) { lastStreakAt = t; events.Add(new DoctorEvent { Game = game, Kind = "slowdown", Title = "Slowdown with no new game work", Important = true, Text = recentSlow.Count + " slow frames in 10 seconds without shader, texture or loading work." }); }
                    }
                    continue;
                }
                if (line.Contains("memory used and budgeted:"))
                {
                    var fullest = Heap.Matches(line).Cast<Match>().Select(m => new { Used = Double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Total = Double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) }).Where(h => h.Total > 0).OrderByDescending(h => h.Used / h.Total).FirstOrDefault();
                    if (fullest == null) continue;
                    double used = fullest.Used, total = fullest.Total;
                    if (total > 0 && used / total > .92 && t - lastMemoryAt > 120) { lastMemoryAt = t; events.Add(new DoctorEvent { Game = game, Kind = "memory", Title = "Graphics memory almost full", Important = true, Text = used + " of " + total + " MB in use. A lower render scale reduces this." }); }
                    continue;
                }
                if (Regex.IsMatch(line, @"\bFATAL:|\*\*\* Crash|No new frame for \d+ seconds"))
                {
                    Crashed = true;
                    events.Add(new DoctorEvent { Game = game, Kind = "crash", Title = line.Contains("No new frame") ? "The game stopped drawing" : "The game reported a crash", Important = true, Text = line.Substring(Math.Max(0, line.IndexOf(']') + 1)).Trim() });
                }
            }
            return events;
        }
    }

    /// <summary>
    /// Watches converted games while they run and acts when they close: it reads the game's log as it is written,
    /// explains each slowdown (including what else was using the CPU at that moment), and when the game exits it applies
    /// the supported repairs and asks the AI (Codex) for a diagnosis of new problems. It never changes a running game.
    /// </summary>
    public sealed class LiveDoctor : IDisposable
    {
        private sealed class Game { public string Exe; public bool Running, Seen; public long Offset; public LogWatch Watch = new LogWatch(); public string Name { get { return Path.GetFileNameWithoutExtension(Exe); } } }
        private readonly Dictionary<string, Game> games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        private readonly string listPath, reportsRoot;
        private readonly object sync = new object();
        private readonly Timer timer;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private int busy;
        public bool AutoFix = true, UseAi = true, AutoEngine = true;
        public event Action<DoctorEvent> Reported;
        public Func<string, bool> IsBusyElsewhere = exe => false;
        public static readonly string[] SkipProcesses = { "Idle", "System", "_Total", "dwm", "csrss", "audiodg", "Memory Compression" };

        public static LiveDoctor Current { get; private set; }
        public LiveDoctor(string appFolder)
        {
            Current = this;
            listPath = Path.Combine(appFolder, "watched-games.txt"); reportsRoot = Path.Combine(appFolder, "Diagnostics");
            try { if (File.Exists(listPath)) foreach (string line in File.ReadAllLines(listPath)) Add(line.Trim(), false); } catch (IOException) { }
            try { string last = Path.Combine(appFolder, "last-game.txt"); if (File.Exists(last)) Watch(File.ReadAllText(last).Trim()); } catch (IOException) { }
            timer = new Timer(delegate { Tick(); }, null, 1500, 3000);
        }

        public string[] Games { get { lock (sync) return games.Keys.ToArray(); } }

        private Timer discoverTimer;
        private static readonly string[] SkipFolders = { "_data", "_build", "romfs", "exefs", "shader_cache", "PortForge-engine-backup", ".git", "node_modules", "AppData", "Windows", "$Recycle.Bin", "Program Files", "Program Files (x86)" };

        /// <summary>The folders where converted games are usually kept: the desktop, documents, downloads, the games folders and the app's own output.</summary>
        public static string[] DefaultDiscoveryRoots(string appFolder)
        {
            var roots = new List<string> { Path.Combine(appFolder, "NativeGames") };
            foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile })
                try { roots.Add(Environment.GetFolderPath(folder)); } catch (Exception) { }
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games")); } catch (Exception) { }
            foreach (var drive in DriveInfo.GetDrives()) try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) { roots.Add(Path.Combine(drive.Name, "Games")); roots.Add(Path.Combine(drive.Name, "Downloads")); } } catch (Exception) { }
            return roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>Converted games under the roots, up to three folders deep: a game's .exe beside its .abi and _code.dll. Any game made by PortForge or ScarletNX is found, whatever its name.</summary>
        public static List<string> FindConvertedGames(IEnumerable<string> roots, int maxDepth = 3, int limit = 200)
        {
            var found = new List<string>();
            foreach (string root in roots) Scan(root, 0, maxDepth, limit, found);
            return found;
        }
        private static void Scan(string folder, int depth, int maxDepth, int limit, List<string> found)
        {
            if (found.Count >= limit) return;
            try
            {
                foreach (string abi in Directory.GetFiles(folder, "*.abi"))
                {
                    string stem = Path.Combine(folder, Path.GetFileNameWithoutExtension(abi));
                    if (File.Exists(stem + ".exe") && File.Exists(stem + "_code.dll")) found.Add(stem + ".exe");
                }
                if (depth >= maxDepth) return;
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".", StringComparison.Ordinal) || SkipFolders.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                    Scan(sub, depth + 1, maxDepth, limit, found);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        /// <summary>Starts watching every converted game on this PC, now and every half hour, so each one gets the PortForge Engine and the doctor's repairs without the player loading it first.</summary>
        public void StartDiscovery(IEnumerable<string> roots, int firstDelayMs = 8000)
        {
            var list = roots.ToArray();
            discoverTimer = new Timer(delegate { try { foreach (string exe in FindConvertedGames(list)) Watch(exe); } catch (Exception) { } }, null, firstDelayMs, 30 * 60 * 1000);
        }

        public void Watch(string exe)
        {
            if (Add(exe, true)) Report(new DoctorEvent { Game = Path.GetFileNameWithoutExtension(exe), Kind = "watch", Title = "Now watching this game" });
        }

        private bool Add(string exe, bool save)
        {
            if (String.IsNullOrWhiteSpace(exe)) return false;
            try { exe = Path.GetFullPath(exe); } catch (Exception) { return false; }
            if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            lock (sync)
            {
                if (games.ContainsKey(exe)) return false;
                games[exe] = new Game { Exe = exe };
                if (save) try { File.WriteAllLines(listPath, games.Keys); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return true;
        }

        private void Report(DoctorEvent e)
        {
            try
            {
                string folder = null; lock (sync) { var g = games.Values.FirstOrDefault(x => x.Name == e.Game); if (g != null) folder = Path.GetDirectoryName(g.Exe); }
                if (folder != null) File.AppendAllText(Path.Combine(folder, "PortForge-doctor.log"), e + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            var handler = Reported; if (handler != null) handler(e);
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                foreach (var game in games.Values.ToArray())
                {
                    if (stop.IsCancellationRequested) return;
                    bool running = GameDiagnostics.Running(game.Exe);
                    string log = Path.ChangeExtension(game.Exe, ".log");
                    if (running && !game.Running)
                    {
                        game.Running = true; game.Offset = 0; game.Watch = new LogWatch();
                        Report(new DoctorEvent { Game = game.Name, Kind = "start", Title = "Game started", Text = "Watching for stutter, slowdowns and crashes." });
                    }
                    bool first = !game.Seen; game.Seen = true;
                    if (running) ReadNew(game, log);
                    else if (first) AfterSession(game.Exe);
                    else if (game.Running)
                    {
                        game.Running = false; ReadNew(game, log);
                        var w = game.Watch;
                        Report(new DoctorEvent { Game = game.Name, Kind = "end", Title = "Game closed", Text = "Session summary: " + w.CompileFrames + " shader-stutter frames (" + w.CompileWaitMs + " ms waited), " + w.LoadingFrames + " loading freezes, " + w.UnexplainedFrames + " other slow frames, worst " + w.WorstMs + " ms." });
                        AfterSession(game.Exe);
                    }
                }
            }
            catch (Exception e) { Report(new DoctorEvent { Kind = "error", Title = "The doctor hit a problem", Text = e.Message }); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        private void ReadNew(Game game, string log)
        {
            if (!File.Exists(log)) return;
            using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < game.Offset) { game.Offset = 0; game.Watch = new LogWatch(); }
                if (stream.Length == game.Offset) return;
                stream.Seek(game.Offset, SeekOrigin.Begin);
                var buffer = new byte[Math.Min(4 * 1024 * 1024, stream.Length - game.Offset)];
                int read = stream.Read(buffer, 0, buffer.Length); game.Offset += read;
                foreach (var e in game.Watch.Feed(Encoding.UTF8.GetString(buffer, 0, read), game.Name))
                {
                    if (e.Kind == "slowdown" && game.Running) e.Text += " " + WhatElseIsBusy(game.Exe);
                    Report(e);
                }
            }
        }

        /// <summary>Samples which other programs use the CPU right now, and whether the game is the active window.</summary>
        public static string WhatElseIsBusy(string gameExe)
        {
            var first = new Dictionary<int, TimeSpan>(); var names = new Dictionary<int, string>();
            foreach (var p in Process.GetProcesses()) { try { first[p.Id] = p.TotalProcessorTime; names[p.Id] = p.ProcessName; } catch (Exception) { } finally { p.Dispose(); } }
            var watch = Stopwatch.StartNew(); Thread.Sleep(1000);
            var busy = new List<KeyValuePair<string, double>>();
            string gameName = Path.GetFileNameWithoutExtension(gameExe);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    TimeSpan before;
                    if (!first.TryGetValue(p.Id, out before) || SkipProcesses.Contains(p.ProcessName) || p.ProcessName == gameName || p.Id == Process.GetCurrentProcess().Id) continue;
                    double share = (p.TotalProcessorTime - before).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
                    if (share >= 5) busy.Add(new KeyValuePair<string, double>(p.ProcessName, share));
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            string text = busy.Count == 0 ? "No other program was using much CPU." : "Other programs using the CPU then: " + String.Join(", ", busy.OrderByDescending(b => b.Value).Take(3).Select(b => b.Key + " " + b.Value.ToString("0") + "%")) + ".";
            try
            {
                uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                using (var active = Process.GetProcessById((int)pid)) if (active.ProcessName != gameName) text += " The game was not the active window (" + active.ProcessName + " was), which can make Windows give it less time.";
            }
            catch (Exception) { }
            return text;
        }

        private static DateTime aiPausedUntil = DateTime.MinValue;
        private static string aiPauseReason = "";
        /// <summary>Why the extra AI review is not running right now, for the doctor's window ("" when it can run).</summary>
        public static string AiStatus
        {
            get
            {
                if (CodexDiagnosis.Find() == null) return "Extra AI review: Codex isn't installed on this PC (optional).";
                return DateTime.UtcNow < aiPausedUntil ? "Extra AI review paused until " + aiPausedUntil.ToLocalTime().ToString("HH:mm") + ": " + aiPauseReason : "";
            }
        }

        /// <summary>
        /// When a game closes: put the PortForge Engine under it (the stutter fix, on any PC), apply the supported repairs
        /// (with backups), then, only as an optional extra, ask Codex about new problems. Each step works on its own: a
        /// failed or unavailable AI never stops the built-in fixes.
        /// </summary>
        private void AfterSession(string exe)
        {
            if (stop.IsCancellationRequested) return;
            string name = Path.GetFileNameWithoutExtension(exe);
            if (IsBusyElsewhere(exe)) { Report(new DoctorEvent { Game = name, Kind = "skip", Title = "Repairs postponed", Text = "Another PortForge window is working on this game." }); return; }
            RuntimeCapabilities cap = null;
            if (AutoFix && AutoEngine) InstallEngine(exe, name);
            if (AutoFix)
            {
                try
                {
                    cap = RuntimeCapabilities.Read(exe, stop.Token);
                    var health = GameDiagnostics.Scan(exe, cap, null);
                    if (health.Issues.Any(i => i.Fix != null))
                    {
                        Report(new DoctorEvent { Game = name, Kind = "fixing", Title = "Fixing " + health.Issues.Count(i => i.Fix != null) + " problem(s)", Text = String.Join("; ", health.Issues.Where(i => i.Fix != null).Select(i => i.Title)) + ". Your save and settings are backed up first." });
                        string done = GameDiagnostics.Repair(exe, cap, null, null, true, new Progress<string>(m => Report(new DoctorEvent { Game = name, Kind = "progress", Title = "Working", Text = m })), stop.Token);
                        Report(new DoctorEvent { Game = name, Kind = "fixed", Title = "Fixed automatically", Fixed = true, Important = true, Text = done.Replace("\r\n", " ") });
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { Report(new DoctorEvent { Game = name, Kind = "error", Title = "A built-in repair could not finish", Important = true, Text = e.Message + " Settings and saves stay as their backups had them." }); }
            }
            if (UseAi) AiReview(exe, name, cap);
        }

        private void InstallEngine(string exe, string name)
        {
            var package = EngineManager.Find();
            if (package == null || EngineManager.IsInstalled(exe, package) || EngineManager.Declined(exe) != null) return;
            try
            {
                Report(new DoctorEvent { Game = name, Kind = "engine", Title = "Installing the PortForge Engine", Text = "It removes shader stutter. The original engine is kept, and PortForge checks the game still starts." });
                string done = EngineManager.Install(exe, package, null, stop.Token);
                Report(new DoctorEvent { Game = name, Kind = "fixed", Title = "PortForge Engine installed", Fixed = true, Important = true, Text = done });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Report(new DoctorEvent { Game = name, Kind = "engine", Title = "Kept the original engine", Important = true, Text = e.Message + " The doctor won't try again for this game; you can still install it from the Engine tab." }); }
        }

        /// <summary>The optional AI review. Without Codex, or while its account is limited, the doctor goes on without it.</summary>
        private void AiReview(string exe, string name, RuntimeCapabilities cap)
        {
            if (CodexDiagnosis.Find() == null || DateTime.UtcNow < aiPausedUntil) return;
            try
            {
                cap = cap ?? RuntimeCapabilities.Read(exe, stop.Token);
                var health = GameDiagnostics.Scan(exe, cap, null);
                string signature = CodexDiagnosis.Fingerprint(health);
                if (signature == null) return;
                Directory.CreateDirectory(reportsRoot);
                string cache = Path.Combine(reportsRoot, "last-ai-" + signature + ".json");
                if (File.Exists(cache)) return;
                Report(new DoctorEvent { Game = name, Kind = "ai", Title = "Extra AI review", Text = "Codex reads a short summary of the log (no files, keys or paths) and can pick from the supported repairs." });
                var ai = CodexDiagnosis.Review(health, reportsRoot, stop.Token);
                File.Copy(ai.ReportPath, cache, true);
                Report(new DoctorEvent { Game = name, Kind = "ai", Title = "AI diagnosis", Important = true, Text = ai.summary + (ai.needs_engine_fix ? " Engine note: " + ai.engineering_note : "") });
                if (AutoFix && ai.recommended_rules.Length > 0)
                {
                    string done = GameDiagnostics.Repair(exe, cap, null, ai.recommended_rules, true, null, stop.Token);
                    Report(new DoctorEvent { Game = name, Kind = "fixed", Title = "AI-recommended repair applied", Fixed = true, Important = true, Text = done.Replace("\r\n", " ") });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                string why = AiProblem(e.Message);
                aiPausedUntil = DateTime.UtcNow.AddHours(why.Contains("usage limit") || why.Contains("signed in") ? 3 : 0.5);
                aiPauseReason = why;
                Report(new DoctorEvent { Game = name, Kind = "ai-skipped", Title = "Extra AI review skipped", Text = why + " The built-in checks and fixes work without it." });
            }
        }

        /// <summary>A plain explanation of why Codex could not help, from its error text.</summary>
        public static string AiProblem(string error)
        {
            string e = error ?? "";
            if (e.IndexOf("usage limit", StringComparison.OrdinalIgnoreCase) >= 0) return "Your Codex account has reached its usage limit for now; the doctor will try again later.";
            if (e.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0) return "Codex is not signed in on this PC (sign in to Codex to use the extra AI review).";
            if (e.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) return "Codex took too long to answer.";
            if (e.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0) return "Codex could not reach its service (check the internet connection).";
            return "Codex could not finish the review.";
        }

        public void Dispose() { stop.Cancel(); timer.Dispose(); if (discoverTimer != null) discoverTimer.Dispose(); if (Current == this) Current = null; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}

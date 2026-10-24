using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PortForge
{
    /// <summary>The PortForge Engine as shipped in the app's Engine folder: its runtime and the libraries beside it.</summary>
    public sealed class EnginePackage
    {
        public string Folder, Exe, Version = "", Base = "", Features = "";
        public string[] Libraries = new string[0];
        public string Name { get { return "PortForge Engine " + Version; } }
    }

    /// <summary>One measured run of a game, read from the engine's own log lines.</summary>
    public sealed class EngineRun
    {
        public string Label = "";
        public int Frames, SlowFrames, WorstMs, WaitedMs, AsyncDrops, PipelinesMade;
        public bool Crashed;
        public string Summary { get { return Label + ": " + Frames + " frames, " + SlowFrames + " frames over 50 ms (worst " + WorstMs + " ms), " + WaitedMs + " ms waiting for shaders, " + PipelinesMade + " new pipelines" + (AsyncDrops > 0 ? ", " + AsyncDrops + " draws briefly skipped" : "") + (Crashed ? ", CRASHED" : ""); } }
    }

    public static class EngineManager
    {
        public const string BackupFolderName = "PortForge-engine-backup";
        /// <summary>Tests point this at a package of their own; the app uses its Engine folder.</summary>
        public static string FolderOverride;
        public static string DefaultFolder { get { return FolderOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Engine"); } }

        public static EnginePackage Find(string folder = null)
        {
            folder = folder ?? DefaultFolder;
            string exe = Path.Combine(folder, "scarletnx.exe"), info = Path.Combine(folder, "ENGINE.txt");
            if (!File.Exists(exe) || !File.Exists(info)) return null;
            var package = new EnginePackage { Folder = folder, Exe = exe };
            foreach (string line in File.ReadAllLines(info))
            {
                int eq = line.IndexOf('='); if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "version") package.Version = value; else if (key == "base") package.Base = value; else if (key == "features") package.Features = value;
            }
            package.Libraries = Directory.GetFiles(folder, "*.dll").Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            return package;
        }

        public static bool IsInstalled(string gameExe, EnginePackage package)
        {
            return package != null && File.Exists(gameExe) && new FileInfo(gameExe).Length == new FileInfo(package.Exe).Length &&
                   GameDownloads.HashFile(gameExe, CancellationToken.None) == GameDownloads.HashFile(package.Exe, CancellationToken.None);
        }

        public static string Describe(string gameExe, EnginePackage package)
        {
            if (!File.Exists(gameExe)) return "No game loaded.";
            if (package == null) return "The PortForge Engine is not included in this copy of the app (Engine folder missing).";
            if (IsInstalled(gameExe, package)) return "This game runs on the " + package.Name + ".";
            bool backup = File.Exists(Path.Combine(Path.GetDirectoryName(gameExe), BackupFolderName, Path.GetFileName(gameExe)));
            string marker = Path.Combine(Path.GetDirectoryName(gameExe), "ENGINE.txt");
            string have = "";
            if (File.Exists(marker))
            {
                foreach (string line in File.ReadAllLines(marker))
                    if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase)) { have = line.Substring(8).Trim(); break; }
            }
            return "This game needs the " + package.Name + (have.Length > 0 ? " (it still has Engine " + have + ")" : " (its scarletnx.exe does not match the app Engine folder)") +
                   (backup ? ". A backup of the previous engine is kept." : ". Use Install, or let the Doctor update it when the game is closed.");
        }

        /// <summary>Puts the PortForge Engine in place of the game's runtime, keeping the original, then proves the game still starts.</summary>
        public static string Install(string gameExe, EnginePackage package, IProgress<string> progress, CancellationToken token)
        {
            if (package == null) throw new IOException("The PortForge Engine is not included in this copy of the app.");
            gameExe = Path.GetFullPath(gameExe); GameOptimization.ValidateGame(gameExe); GameOptimization.EnsureStopped(gameExe);
            string folder = Path.GetDirectoryName(gameExe), backup = Path.Combine(folder, BackupFolderName);
            if (!File.Exists(Path.Combine(folder, Path.GetFileNameWithoutExtension(gameExe) + "_code.dll"))) throw new IOException("This is not a converted game: its _code.dll is missing.");
            if (IsInstalled(gameExe, package)) { CopyLicenses(package.Folder, folder, token); CopyEngineInfo(package, folder, token); return "The " + package.Name + " is already installed for this game."; }
            using (PlayFirst.Begin(gameExe)) return InstallUnheld(gameExe, folder, backup, package, progress, token);
        }
        /// <summary>True when the game exe is missing or does not match the app's Engine package (size or hash).</summary>
        public static bool NeedsUpdate(string gameExe, EnginePackage package)
        {
            return package != null && File.Exists(gameExe) && !IsInstalled(gameExe, package);
        }
        /// <summary>
        /// Before Play: copy the app Engine into the game folder when it does not match. Games that already have
        /// ENGINE.txt get a fast copy (no 15s prove). First-time installs still use Install's startup check.
        /// Returns null when nothing was needed; otherwise a short status line.
        /// </summary>
        public static string EnsureForPlay(string gameExe, IProgress<string> progress, CancellationToken token)
        {
            var package = Find();
            if (package == null || !File.Exists(gameExe) || IsInstalled(gameExe, package) || Declined(gameExe) != null)
                return null;
            gameExe = Path.GetFullPath(gameExe);
            GameOptimization.ValidateGame(gameExe);
            GameOptimization.EnsureStopped(gameExe);
            string folder = Path.GetDirectoryName(gameExe);
            if (!File.Exists(Path.Combine(folder, "ENGINE.txt")))
                return Install(gameExe, package, progress, token);
            using (PlayFirst.Begin(gameExe))
            {
                if (progress != null) progress.Report("Updating this game to the " + package.Name + " before launch.");
                string backup = Path.Combine(folder, BackupFolderName);
                if (!File.Exists(Path.Combine(backup, Path.GetFileName(gameExe))))
                {
                    Directory.CreateDirectory(backup);
                    File.Copy(gameExe, Path.Combine(backup, Path.GetFileName(gameExe)), false);
                    foreach (string lib in package.Libraries)
                    {
                        string own = Path.Combine(folder, lib);
                        if (File.Exists(own)) File.Copy(own, Path.Combine(backup, lib), false);
                    }
                    File.WriteAllLines(Path.Combine(backup, "added.txt"), package.Libraries.Where(lib => !File.Exists(Path.Combine(folder, lib))));
                }
                foreach (string lib in package.Libraries) CopyVerified(Path.Combine(package.Folder, lib), Path.Combine(folder, lib), token);
                CopyLicenses(package.Folder, folder, token);
                CopyEngineInfo(package, folder, token);
                CopyVerified(package.Exe, gameExe, token);
            }
            return "Updated this game to the " + package.Name + " for this launch.";
        }
        private static void CopyEngineInfo(EnginePackage package, string folder, CancellationToken token)
        {
            string info = Path.Combine(package.Folder, "ENGINE.txt");
            if (File.Exists(info)) CopyVerified(info, Path.Combine(folder, "ENGINE.txt"), token);
        }
        private static string InstallUnheld(string gameExe, string folder, string backup, EnginePackage package, IProgress<string> progress, CancellationToken token)
        {
            // The first install keeps the original engine; later installs never overwrite that copy.
            if (!File.Exists(Path.Combine(backup, Path.GetFileName(gameExe))))
            {
                Directory.CreateDirectory(backup);
                File.Copy(gameExe, Path.Combine(backup, Path.GetFileName(gameExe)), false);
                foreach (string lib in package.Libraries) { string own = Path.Combine(folder, lib); if (File.Exists(own)) File.Copy(own, Path.Combine(backup, lib), false); }
                File.WriteAllLines(Path.Combine(backup, "added.txt"), package.Libraries.Where(lib => !File.Exists(Path.Combine(folder, lib))));
            }
            if (progress != null) progress.Report("Installing the " + package.Name + " (the original engine is kept in " + BackupFolderName + ").");
            try
            {
                foreach (string lib in package.Libraries) CopyVerified(Path.Combine(package.Folder, lib), Path.Combine(folder, lib), token);
                CopyLicenses(package.Folder, folder, token);
                CopyEngineInfo(package, folder, token);
                CopyVerified(package.Exe, gameExe, token);
                if (progress != null) progress.Report("Checking that the game starts on the new engine (about 15 seconds). Your save and settings are protected.");
                string output = TimedRun(gameExe, "-H --seconds=4 --log=1 --fullscreen=0", null, token);
                if (!Regex.IsMatch(output, @"(?m)^frames=\d+")) throw new IOException("The game did not draw any frames on the new engine.");
            }
            catch (OperationCanceledException) { Restore(gameExe); throw; }
            catch (Exception e) { Restore(gameExe); Decline(gameExe, "The PortForge Engine could not run this game: " + e.Message); throw new IOException("The PortForge Engine could not run this game, so the original engine was put back. " + e.Message, e); }
            try { File.Delete(Path.Combine(backup, DeclinedFile)); } catch (IOException) { }
            return "Installed the " + package.Name + " and checked that the game starts. Smart async shaders are on; turn them off in this tab if something looks wrong.";
        }

        public const string DeclinedFile = "keep-original-engine.txt";
        public static void CopyLicenses(string engineFolder, string gameFolder, CancellationToken token)
        {
            string source = Path.Combine(engineFolder, "licenses");
            if (!Directory.Exists(source)) return;
            string target = Path.Combine(gameFolder, "PortForge-engine-licenses");
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source, "*.txt"))
            {
                string destination = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(destination) || GameDownloads.HashFile(file, token) != GameDownloads.HashFile(destination, token)) CopyVerified(file, destination, token);
            }
        }
        /// <summary>Why the doctor should not put the PortForge Engine under this game again (the player chose the original, or it failed), or null.</summary>
        public static string Declined(string gameExe)
        {
            string marker = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe)), BackupFolderName, DeclinedFile);
            try { return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null; } catch (IOException) { return "unreadable marker"; }
        }
        public static void Decline(string gameExe, string reason)
        {
            string backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe)), BackupFolderName);
            Directory.CreateDirectory(backup); File.WriteAllText(Path.Combine(backup, DeclinedFile), reason);
        }
        public static void Restore(string gameExe)
        {
            gameExe = Path.GetFullPath(gameExe); GameOptimization.EnsureStopped(gameExe);
            string folder = Path.GetDirectoryName(gameExe), backup = Path.Combine(folder, BackupFolderName);
            string original = Path.Combine(backup, Path.GetFileName(gameExe));
            if (!File.Exists(original)) throw new IOException("There is no original engine backup for this game.");
            foreach (string file in Directory.GetFiles(backup))
            {
                string name = Path.GetFileName(file);
                if (name == "added.txt") continue;
                CopyVerified(file, Path.Combine(folder, name), CancellationToken.None);
            }
            string added = Path.Combine(backup, "added.txt");
            if (File.Exists(added)) foreach (string lib in File.ReadAllLines(added)) { string path = Path.Combine(folder, lib.Trim()); if (lib.Trim().Length > 0 && File.Exists(path)) File.Delete(path); }
        }

        private static void CopyVerified(string source, string target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string temp = target + ".portforge-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temp, true);
                if (GameDownloads.HashFile(source, token) != GameDownloads.HashFile(temp, token)) throw new IOException("Copy verification failed for " + Path.GetFileName(target) + ".");
                if (File.Exists(target)) ConversionFiles.ReplaceWithRetry(temp, target, token, null); else File.Move(temp, target);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
        }

        /// <summary>Runs the game hidden for a timed test with its save, log and settings protected and put back afterwards.</summary>
        public static string TimedRun(string runtime, string arguments, string gameExe, CancellationToken token, int seconds = 6)
        {
            string game = gameExe ?? runtime;
            string config = GameOptimization.ConfigPath(game);
            byte[] ini = File.Exists(config) ? File.ReadAllBytes(config) : null;
            using (new GameStateGuard(game))
            {
                try
                {
                    var duration = Regex.Match(arguments, @"--seconds=(\d+)");
                    int wait = (duration.Success ? Int32.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture) : seconds) + 60;
                    return GameOptimization.Run(runtime, arguments + (gameExe != null ? " --game=" + GameManifest.Quote(Path.GetFullPath(gameExe)) : ""), wait, token);
                }
                finally
                {
                    GameOptimization.EnsureStopped(game);
                    if (ini != null) GameOptimization.AtomicWrite(config, ini, null);
                }
            }
        }

        public static EngineRun Parse(string label, string output)
        {
            var run = new EngineRun { Label = label };
            var frames = Regex.Match(output, @"(?m)^frames=(\d+)"); if (frames.Success) run.Frames = Int32.Parse(frames.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (var f in GameDiagnostics.StallFrames(output)) { run.SlowFrames++; run.WorstMs = Math.Max(run.WorstMs, f.FrameMs); }
            var totals = Regex.Matches(output, @"(\d+) pipelines made \(\d+ from the cache, \d+ ms making, (\d+) ms waited for\)").Cast<Match>().LastOrDefault();
            if (totals != null) { run.PipelinesMade = Int32.Parse(totals.Groups[1].Value, CultureInfo.InvariantCulture); run.WaitedMs = Int32.Parse(totals.Groups[2].Value, CultureInfo.InvariantCulture); }
            var drops = Regex.Matches(output, @"(\d+) binds dropped their draws").Cast<Match>().LastOrDefault();
            if (drops != null) run.AsyncDrops = Int32.Parse(drops.Groups[1].Value, CultureInfo.InvariantCulture);
            run.Crashed = Regex.IsMatch(output, @"\bFATAL:|\*\*\* Crash");
            return run;
        }

        /// <summary>
        /// The fair test of the engine's stutter fix: the same scripted gameplay twice with an empty shader cache, once with every
        /// draw waiting for its pipeline (the original behaviour) and once with smart async shaders. The player's shader cache,
        /// save, log and settings are put back afterwards.
        /// </summary>
        public static EngineRun[] CompareAsync(string runtime, string gameExe, IProgress<string> progress, CancellationToken token)
        {
            string cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe)), "shader_cache"), aside = cache + ".portforge-ab-" + Guid.NewGuid().ToString("N");
            bool hadCache = Directory.Exists(cache);
            var runs = new List<EngineRun>();
            GameOptimization.EnsureStopped(gameExe);
            if (hadCache) Directory.Move(cache, aside);
            try
            {
                foreach (int async in new[] { 0, 1 })
                {
                    token.ThrowIfCancellationRequested();
                    if (Directory.Exists(cache)) Directory.Delete(cache, true);
                    string label = async == 1 ? "Smart async shaders" : "Wait for every shader (original)";
                    if (progress != null) progress.Report("Measuring: " + label + " (" + GameOptimization.GameplaySeconds + " seconds of scripted play with an empty shader cache).");
                    string args = "-H --seconds=" + GameOptimization.GameplaySeconds + " --log=1 --fullscreen=0 --vsync=0 --fps_limit=0 --async_shaders=" + async + " --script=" + GameOptimization.GameplayScript + " --press_len=300";
                    runs.Add(Parse(label, TimedRun(runtime, args, runtime == gameExe ? null : gameExe, token)));
                }
            }
            finally
            {
                try { GameOptimization.EnsureStopped(gameExe); } catch (IOException) { }
                if (Directory.Exists(cache) && hadCache) Directory.Delete(cache, true);
                if (hadCache && Directory.Exists(aside)) Directory.Move(aside, cache);
            }
            return runs.ToArray();
        }
    }
}

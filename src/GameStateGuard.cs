using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Text;

namespace PortForge
{
    /// <summary>
    /// Protects a game's save folder and log while a scripted gameplay test presses buttons.
    /// The save folder and log are copied before the test and put back exactly when it ends, even after a failure or cancel.
    /// </summary>
    public sealed class GameStateGuard : IDisposable
    {
        private readonly string exePath, saveFolder, logPath, backupRoot, savedBackup;
        private readonly bool hadSave;
        private readonly byte[] logBytes;
        private bool disposed;
        private Mutex syncMutex;
        private static readonly Dictionary<string,int> protectedGames = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> liveBackups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static bool IsProtected(string exe) { lock(protectedGames) return protectedGames.ContainsKey(Path.GetFullPath(exe)); }
        private void ReleaseProtection()
        {
            lock(protectedGames) { int count; if(protectedGames.TryGetValue(exePath,out count)) { if(count==1)protectedGames.Remove(exePath);else protectedGames[exePath]=count-1; } if(backupRoot!=null)liveBackups.Remove(backupRoot); }
            if(syncMutex!=null){syncMutex.ReleaseMutex();syncMutex.Dispose();syncMutex=null;}
        }

        public GameStateGuard(string exe)
        {
            string full = exePath = Path.GetFullPath(exe), folder = Path.GetDirectoryName(full), stem = Path.GetFileNameWithoutExtension(full);
            if (PlayFirst.Holding(full)) throw new OperationCanceledException("The player started this game. PortForge's test will run another time.");
            syncMutex = new Mutex(false,"Local\\PortForge.CloudSync."+CloudSaveService.Digest(Encoding.UTF8.GetBytes(full.ToLowerInvariant())).Substring(0,24));
            bool held;try{held=syncMutex.WaitOne(30000);}catch(AbandonedMutexException){held=true;}
            if(!held){syncMutex.Dispose();syncMutex=null;throw new IOException("Cloud saves are busy. Retry the protected game test shortly.");}
            lock(protectedGames){int count;protectedGames.TryGetValue(full,out count);protectedGames[full]=count+1;}
            try {
            saveFolder = Path.Combine(folder, stem + "_data", "save");
            logPath = Path.ChangeExtension(full, ".log");
            backupRoot = Path.Combine(folder, "PortForge-test-backup-" + Guid.NewGuid().ToString("N"));
            lock(protectedGames) liveBackups.Add(backupRoot);
            hadSave = Directory.Exists(saveFolder);
            logBytes = File.Exists(logPath) ? File.ReadAllBytes(logPath) : null;
            Directory.CreateDirectory(backupRoot);
            savedBackup = Path.Combine(backupRoot, "save");
            if (hadSave) CopyFolder(saveFolder, savedBackup);
            } catch { ReleaseProtection();throw; }
        }

        public string BackupFolder { get { return backupRoot; } }

        public const string SaveArchiveName = "PortForge-save-backups";
        /// <summary>
        /// A test backup no live test owns was left by a test that was interrupted (the app closed or crashed mid-test). It would
        /// keep cloud sync and Play waiting forever, so it is resolved here, and nothing is deleted that differs from the save:
        /// a backup equal to the save is removed; a save changed only during the test window gets its pre-test copy back (the
        /// test's copy is kept); a save the player changed later stays, with the pre-test copy kept. Returns what was done, or null.
        /// </summary>
        public static string RecoverStale(string exe)
        {
            string full = Path.GetFullPath(exe), folder = Path.GetDirectoryName(full), save = CloudSaveService.SaveFolder(full);
            string[] stale;
            lock (protectedGames)
            {
                if (protectedGames.ContainsKey(full)) return null;
                stale = Directory.GetDirectories(folder, "PortForge-test-backup-*").Where(d => !liveBackups.Contains(Path.GetFullPath(d))).OrderBy(d => Directory.GetCreationTimeUtc(d)).ToArray();
            }
            if (stale.Length == 0 || GameDiagnostics.Running(full)) return null;
            var done = new List<string>();
            using (var mutex = new Mutex(false, "Local\\PortForge.CloudSync." + CloudSaveService.Digest(Encoding.UTF8.GetBytes(full.ToLowerInvariant())).Substring(0, 24)))
            {
                bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) return null; // a test in another PortForge holds the save
                try
                {
                    string archive = Path.Combine(folder, SaveArchiveName);
                    foreach (string dir in stale)
                    {
                        string stamp = Directory.GetCreationTime(dir).ToString("yyyyMMdd-HHmmss"), backupSave = Path.Combine(dir, "save");
                        if (!Directory.Exists(backupSave) || !Directory.EnumerateFileSystemEntries(backupSave).Any())
                        {
                            if (!Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any()) { Directory.Delete(dir, true); done.Add("Removed an empty backup left by an interrupted test."); }
                            else { Directory.CreateDirectory(archive); Directory.Move(dir, Unique(Path.Combine(archive, "interrupted-test-" + stamp))); done.Add("Moved an interrupted test's files to " + SaveArchiveName + "."); }
                            continue;
                        }
                        string before = CloudSaveService.TreeHash(backupSave, CancellationToken.None), now = Directory.Exists(save) ? CloudSaveService.TreeHash(save, CancellationToken.None) : null;
                        if (before == now) { Directory.Delete(dir, true); done.Add("Removed a leftover test backup: it matched your save exactly."); continue; }
                        DateTime newest = now == null ? DateTime.MinValue : Directory.EnumerateFiles(save, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                        Directory.CreateDirectory(archive);
                        if (now == null || newest <= Directory.GetCreationTimeUtc(dir).AddMinutes(10))
                        {
                            string aside = Unique(Path.Combine(archive, "test-progress-" + stamp));
                            if (Directory.Exists(save)) Directory.Move(save, aside);
                            try { Directory.Move(backupSave, save); }
                            catch { if (!Directory.Exists(save) && Directory.Exists(aside)) Directory.Move(aside, save); throw; }
                            Directory.Delete(dir, true);
                            done.Add("An interrupted test had changed your save. Your save from before the test is back; the test's copy is kept in " + SaveArchiveName + ".");
                        }
                        else
                        {
                            Directory.Move(dir, Unique(Path.Combine(archive, "before-test-" + stamp)));
                            done.Add("Kept your current save (you played after an interrupted test). The save from before that test is kept in " + SaveArchiveName + ".");
                        }
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
            if (done.Count == 0) return null;
            try { File.AppendAllText(Path.Combine(folder, "PortForge-doctor.log"), DateTime.Now.ToString("u") + " " + String.Join(" ", done) + Environment.NewLine); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return String.Join(" ", done);
        }
        private static string Unique(string path) { string candidate = path; for (int i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++) candidate = path + "-" + i; return candidate; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                GameOptimization.EnsureStopped(exePath);
                if (hadSave)
                {
                    // Swap rather than delete first, so the original save is never missing if something fails midway.
                    string sideline = saveFolder + ".pf-test-" + Guid.NewGuid().ToString("N");
                    if (Directory.Exists(saveFolder)) Directory.Move(saveFolder, sideline);
                    try { CopyFolder(savedBackup, saveFolder); }
                    catch { if (!Directory.Exists(saveFolder) && Directory.Exists(sideline)) Directory.Move(sideline, saveFolder); throw; }
                    if (Directory.Exists(sideline)) Directory.Delete(sideline, true);
                }
                else if (Directory.Exists(saveFolder)) Directory.Delete(saveFolder, true);
                if (logBytes != null) File.WriteAllBytes(logPath, logBytes);
                else if (File.Exists(logPath)) File.Delete(logPath);
                if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            }
            catch (Exception e)
            {
                throw new IOException("Could not put the game's save and log back after testing. Your original save is kept in: " + backupRoot, e);
            }
            finally { ReleaseProtection(); }
        }

        private static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source)) CopyFolder(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    /// <summary>
    /// Playing comes first. Play stops PortForge's own background game tests and engine work for that game (each puts the save,
    /// settings and engine back as it unwinds), and none starts again for a while, so the player never waits for the app.
    /// </summary>
    public static class PlayFirst
    {
        private sealed class Test { public string Folder; public Process Process; public bool Yielded; }
        private sealed class Work : IDisposable
        {
            public string Folder; private bool done;
            public void Dispose() { if (done) return; done = true; lock (gate) work.Remove(this); }
        }
        private static readonly object gate = new object();
        private static readonly List<Test> tests = new List<Test>();
        private static readonly List<Work> work = new List<Work>();
        private static readonly Dictionary<string, DateTime> holds = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public static TimeSpan HoldTime = TimeSpan.FromMinutes(2);
        private static string FolderOf(string exe) { return Path.GetDirectoryName(Path.GetFullPath(exe)).TrimEnd('\\'); }

        /// <summary>A test process of the game whose executable is exe (its folder is the game's).</summary>
        public static object Track(string exe, Process process) { var t = new Test { Folder = FolderOf(exe), Process = process }; lock (gate) tests.Add(t); return t; }
        /// <summary>Stops tracking; true when Play stopped the process.</summary>
        public static bool Release(object handle) { var t = (Test)handle; lock (gate) { tests.Remove(t); return t.Yielded; } }
        /// <summary>Background work on a game's files that Play waits for. Throws OperationCanceledException while Play holds the game.</summary>
        public static IDisposable Begin(string exe)
        {
            var w = new Work { Folder = FolderOf(exe) };
            lock (gate) { if (HoldingLocked(w.Folder)) throw new OperationCanceledException("The player started this game. PortForge will do this another time."); work.Add(w); }
            return w;
        }
        public static bool Holding(string exe) { lock (gate) return HoldingLocked(FolderOf(exe)); }
        private static bool HoldingLocked(string folder)
        {
            DateTime until;
            if (String.Equals(playOwned.Value, folder, StringComparison.OrdinalIgnoreCase)) return false; // Play's own work
            return holds.TryGetValue(folder, out until) && DateTime.UtcNow < until;
        }
        // The game whose Play is doing this work, for the tasks it starts: Play holds back every other background job on
        // the game, but its own engine update and first-launch warm-up are what the player pressed Play for. Without it,
        // Play's engine update met Play's own hold and the start ended as cancelled
        private static readonly System.Threading.AsyncLocal<string> playOwned = new System.Threading.AsyncLocal<string>();
        private sealed class PlayScope : IDisposable
        {
            public string Before; private bool done;
            public void Dispose() { if (done) return; done = true; playOwned.Value = Before; }
        }
        /// <summary>Work Play itself does on exe's game until disposed, and in the tasks started meanwhile: Play's hold doesn't stop it.</summary>
        public static IDisposable ForPlay(string exe)
        {
            var scope = new PlayScope { Before = playOwned.Value };
            playOwned.Value = FolderOf(exe);
            return scope;
        }
        /// <summary>The player pressed Play: stop this game's background tests and hold new background work back for HoldTime.</summary>
        public static int Yield(string exe)
        {
            string folder = FolderOf(exe); var victims = new List<Test>();
            lock (gate)
            {
                holds[folder] = DateTime.UtcNow + HoldTime;
                foreach (var t in tests) if (String.Equals(t.Folder, folder, StringComparison.OrdinalIgnoreCase)) { t.Yielded = true; victims.Add(t); }
            }
            foreach (var t in victims) { try { if (!t.Process.HasExited) t.Process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
            return victims.Count;
        }
        /// <summary>True while a background test, protected save or engine work of this game is still unwinding.</summary>
        public static bool Busy(string exe)
        {
            string folder = FolderOf(exe);
            lock (gate) if (tests.Any(t => String.Equals(t.Folder, folder, StringComparison.OrdinalIgnoreCase)) || work.Any(w => String.Equals(w.Folder, folder, StringComparison.OrdinalIgnoreCase))) return true;
            return GameStateGuard.IsProtected(exe);
        }
    }
}

using System;
using System.IO;
using System.Collections.Generic;
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
        public static bool IsProtected(string exe) { lock(protectedGames) return protectedGames.ContainsKey(Path.GetFullPath(exe)); }
        private void ReleaseProtection()
        {
            lock(protectedGames) { int count; if(protectedGames.TryGetValue(exePath,out count)) { if(count==1)protectedGames.Remove(exePath);else protectedGames[exePath]=count-1; } }
            if(syncMutex!=null){syncMutex.ReleaseMutex();syncMutex.Dispose();syncMutex=null;}
        }

        public GameStateGuard(string exe)
        {
            string full = exePath = Path.GetFullPath(exe), folder = Path.GetDirectoryName(full), stem = Path.GetFileNameWithoutExtension(full);
            syncMutex = new Mutex(false,"Local\\PortForge.CloudSync."+CloudSaveService.Digest(Encoding.UTF8.GetBytes(full.ToLowerInvariant())).Substring(0,24));
            bool held;try{held=syncMutex.WaitOne(30000);}catch(AbandonedMutexException){held=true;}
            if(!held){syncMutex.Dispose();syncMutex=null;throw new IOException("Cloud saves are busy. Retry the protected game test shortly.");}
            lock(protectedGames){int count;protectedGames.TryGetValue(full,out count);protectedGames[full]=count+1;}
            try {
            saveFolder = Path.Combine(folder, stem + "_data", "save");
            logPath = Path.ChangeExtension(full, ".log");
            backupRoot = Path.Combine(folder, "PortForge-test-backup-" + Guid.NewGuid().ToString("N"));
            hadSave = Directory.Exists(saveFolder);
            logBytes = File.Exists(logPath) ? File.ReadAllBytes(logPath) : null;
            Directory.CreateDirectory(backupRoot);
            savedBackup = Path.Combine(backupRoot, "save");
            if (hadSave) CopyFolder(saveFolder, savedBackup);
            } catch { ReleaseProtection();throw; }
        }

        public string BackupFolder { get { return backupRoot; } }

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
}

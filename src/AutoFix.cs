using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PortForge
{
    /// <summary>
    /// "Auto fix everything" for one game, in one click: the PortForge Engine under it, every supported repair, the engine's workers matched to
    /// this PC, and a real check that the game still plays. Everything keeps a backup, and an engine that makes the game crash is taken out again.
    /// What no setting can change is listed at the end, not hidden.
    /// </summary>
    public static class AutoFix
    {
        public static string Run(string game, IProgress<string> progress, CancellationToken token, EnginePackage package = null)
        {
            game = Path.GetFullPath(game); GameOptimization.ValidateGame(game); GameOptimization.EnsureStopped(game);
            string name = Path.GetFileNameWithoutExtension(game);
            var fixedNow = new List<string>(); var fine = new List<string>(); var limits = new List<string>();
            bool installedNow = false;

            // 1. The engine.
            progress.Report("Step 1 of 4: the PortForge Engine.");
            package = package ?? EngineManager.Find();
            if (package == null) limits.Add("The PortForge Engine is not part of this copy of the app (its Engine folder is missing). Update PortForge.");
            else if (EngineManager.IsInstalled(game, package)) fine.Add("The game already runs on the " + package.Name + ".");
            else
            {
                string declined = EngineManager.Declined(game);
                if (declined != null && declined.StartsWith("The player", StringComparison.Ordinal)) limits.Add("The game stays on its original engine because you chose that. Use Install PortForge Engine to change it.");
                else
                {
                    if (declined != null) { try { File.Delete(Path.Combine(Path.GetDirectoryName(game), EngineManager.BackupFolderName, EngineManager.DeclinedFile)); } catch (IOException) { } }
                    try { fixedNow.Add(EngineManager.Install(game, package, progress, token)); installedNow = true; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { limits.Add("The PortForge Engine could not run this game, so the original engine was kept. " + e.Message); }
                }
            }

            // 2. Every repair the doctor knows, with backups.
            progress.Report("Step 2 of 4: looking for problems the app can repair.");
            RuntimeCapabilities cap = null; GameHealth health = new GameHealth();
            try { cap = RuntimeCapabilities.Read(game, token); health = GameDiagnostics.Scan(game, cap, null); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { limits.Add("The game's engine did not describe its settings, so the repair step was skipped: " + e.Message); }
            var repairable = health.Issues.Where(i => i.Fix != null).ToList();
            if (repairable.Count > 0)
            {
                try { fixedNow.Add(GameDiagnostics.Repair(game, cap, null, null, true, progress, token).Replace("\r\n", " ")); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { limits.Add("A repair could not finish: " + e.Message + " Settings stay as the backup had them."); }
            }
            else if (cap != null) fine.Add("No repairable problem was found in the game's log.");

            // 3. The engine's workers for this PC.
            progress.Report("Step 3 of 4: matching the engine to this PC.");
            try
            {
                var pc = HardwareProfile.Detect(); string path = GameOptimization.ConfigPath(game);
                var values = File.Exists(path) ? GameOptimization.ReadValues(File.ReadAllText(path)) : new Dictionary<string, string>();
                var changes = new Dictionary<string, string>(); int now;
                if(cap!=null&&cap.Keys.Contains("frame_guard")&&cap.Keys.Contains("pipeline_threads"))
                {if(!values.ContainsKey("pipeline_threads")||values["pipeline_threads"]!="0")changes["pipeline_threads"]="0";}
                else if (cap != null && cap.Keys.Contains("pipeline_threads") && (!values.ContainsKey("pipeline_threads") || !Int32.TryParse(values["pipeline_threads"], out now) || now < pc.PipelineThreads))
                    changes["pipeline_threads"] = pc.PipelineThreads.ToString(CultureInfo.InvariantCulture);
                if (changes.Count > 0)
                {
                    string text = File.Exists(path) ? File.ReadAllText(path) : "";
                    GameOptimization.AtomicWrite(path, new UTF8Encoding(false).GetBytes(GameOptimization.Merge(text, changes)), File.Exists(path) ? path + ".before-autofix-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak" : null);
                    fixedNow.Add(changes["pipeline_threads"]=="0"?"The engine now chooses shader workers on the PC running the game, on every launch. Previous settings are backed up.":"Set the engine's shader workers to " + changes["pipeline_threads"] + " for this PC (" + pc.LogicalCpus + " CPU threads). The previous settings are backed up.");
                }
                else fine.Add("The engine's shader workers already suit this PC.");
                string async; if (EngineManager.IsInstalled(game, package) && values.TryGetValue("async_shaders", out async) && async == "0") limits.Add("Smart async shaders are switched off for this game. You turned them off; they are the main stutter fix, so turn them back on in this tab if you want it.");
            }
            catch (IOException e) { limits.Add("The settings could not be matched to this PC: " + e.Message); }

            // 4. A real check.
            progress.Report("Step 4 of 4: checking that the game plays (about 25 seconds). Your save and settings are protected.");
            EngineRun run = null;
            try
            {
                string args = "-H --seconds=20 --log=1 --fullscreen=0 --vsync=0 --fps_limit=0 --script=" + GameOptimization.GameplayScript + " --press_len=300";
                run = EngineManager.Parse("Check", EngineManager.TimedRun(game, args, null, token));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { limits.Add("The final check could not run: " + e.Message); }
            string checkLine = null;
            if (run != null)
            {
                if (run.Crashed || run.Frames == 0)
                {
                    if (installedNow) { try { EngineManager.Restore(game); EngineManager.Decline(game, "The PortForge Engine made the game fail the final check."); limits.Add("The game failed the final check on the PortForge Engine, so the original engine was put back."); } catch (Exception e) { limits.Add("The game failed the final check and the original engine could not be restored: " + e.Message); } }
                    else limits.Add("The game did not play in the final check. Open the doctor's list for the reason.");
                }
                else checkLine = "Checked by playing 20 seconds: " + run.Frames + " frames, " + run.SlowFrames + " frame(s) over 50 ms (worst " + run.WorstMs + " ms), " + run.WaitedMs + " ms waiting for shaders.";
            }

            // What is left, from the game's own log.
            foreach (var issue in health.Issues.Where(i => i.Fix == null)) limits.Add(issue.Title + ". " + issue.Evidence);

            var report = new StringBuilder("Auto fix for " + name + " finished.\r\n");
            if (fixedNow.Count > 0) report.Append("FIXED\r\n").Append(String.Join("\r\n", fixedNow.Select(f => "  - " + f).ToArray())).Append("\r\n");
            if (fine.Count > 0) report.Append("ALREADY FINE\r\n").Append(String.Join("\r\n", fine.Select(f => "  - " + f).ToArray())).Append("\r\n");
            if (checkLine != null) report.Append("CHECKED\r\n  - ").Append(checkLine).Append("\r\n");
            if (limits.Count > 0) report.Append("STILL LIMITS THE GAME (no setting changes these)\r\n").Append(String.Join("\r\n", limits.Select(f => "  - " + f).ToArray())).Append("\r\n");
            return report.ToString().TrimEnd();
        }
    }
}

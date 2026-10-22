using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PortForge
{
    /// <summary>
    /// The AI Engine Lab: Codex writes a change to the PortForge Engine's source for a game's recorded problems, PortForge
    /// builds it with the engine's own toolchain and measures it against the current engine on the same scripted play.
    /// The change is kept (committed, packaged into the app's Engine folder) only when it measurably helps without new
    /// errors. Otherwise every edit is reverted. The game itself is never modified by the lab.
    /// </summary>
    public static class EngineLab
    {
        public static readonly string[] AllowedPaths = { "src/nx/gpu/", "src/nx/runtime/settings.txt", "src/nx/runtime/present.c" };
        public const string Bash = @"C:\msys64\usr\bin\bash.exe";

        /// <summary>The engine workspace (holding build-engine.sh, package-engine.sh and src/), from engine-source.txt or next to the app's source.</summary>
        public static string Workspace()
        {
            var candidates = new List<string>();
            foreach (string pointer in new[] { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine-source.txt"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortForge", "engine-source.txt") })
                try { if (File.Exists(pointer)) candidates.Add(File.ReadAllText(pointer).Trim()); } catch (IOException) { }
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent) candidates.Add(Path.Combine(dir.FullName, "engine"));
            return candidates.FirstOrDefault(c => c.Length > 0 && File.Exists(Path.Combine(c, "build-engine.sh")) && Directory.Exists(Path.Combine(c, "src", ".git")));
        }

        public static bool Available() { return Workspace() != null && File.Exists(Bash) && CodexDiagnosis.Find() != null; }

        public static string Availability()
        {
            if (Workspace() == null) return "AI Engine Lab: the engine source isn't on this PC.";
            if (!File.Exists(Bash)) return "AI Engine Lab: the build tools (MSYS2) aren't installed.";
            if (CodexDiagnosis.Find() == null) return "AI Engine Lab: install and sign in to Codex to use it.";
            return "AI Engine Lab is ready.";
        }

        public static string Run(string file, string args, string cwd, int timeoutSeconds, CancellationToken token, string stdin = null, IDictionary<string, string> env = null)
        {
            var info = new ProcessStartInfo(file, args) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null };
            if (env != null) foreach (var e in env) info.EnvironmentVariables[e.Key] = e.Value;
            using (var p = Process.Start(info))
            {
                var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                if (stdin != null) { var bytes = Encoding.UTF8.GetBytes(stdin); p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); p.StandardInput.Close(); }
                var watch = Stopwatch.StartNew();
                try
                {
                    while (!p.WaitForExit(200)) { token.ThrowIfCancellationRequested(); if (watch.Elapsed.TotalSeconds > timeoutSeconds) throw new TimeoutException(Path.GetFileName(file) + " took too long."); }
                    string text = output.Result + "\n" + error.Result;
                    if (p.ExitCode != 0) throw new IOException(Path.GetFileName(file) + " failed (exit " + p.ExitCode + "): " + text.Substring(Math.Max(0, text.Length - 1500)));
                    return text;
                }
                finally
                {
                    if (!p.HasExited) { try { using (var kill = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true })) kill.WaitForExit(5000); } catch (Exception) { p.Kill(); } }
                }
            }
        }

        private static string Git(string src, string args, CancellationToken token) { return Run("git", "-C " + GameManifest.Quote(src) + " " + args, src, 120, token); }
        private static string MsysScript(string ws, string script, string arg, int timeout, CancellationToken token)
        {
            string path = Path.Combine(ws, script).Replace('\\', '/');
            return Run(Bash, "-lc " + GameManifest.Quote("bash '" + path + "'" + (arg != null ? " '" + arg.Replace('\\', '/') + "'" : "")), ws, timeout, token, null, new Dictionary<string, string> { { "MSYSTEM", "CLANG64" }, { "CHERE_INVOKING", "1" } });
        }

        public static EngineRun Measure(string runtime, string game, string label, CancellationToken token)
        {
            string cache = Path.Combine(Path.GetDirectoryName(game), "shader_cache"), aside = cache + ".portforge-lab-" + Guid.NewGuid().ToString("N");
            bool had = Directory.Exists(cache);
            GameOptimization.EnsureStopped(game);
            if (had) Directory.Move(cache, aside);
            try
            {
                string args = "-H --seconds=" + GameOptimization.GameplaySeconds + " --log=1 --fullscreen=0 --vsync=0 --fps_limit=0 --script=" + GameOptimization.GameplayScript + " --press_len=300";
                return EngineManager.Parse(label, EngineManager.TimedRun(runtime, args, game, token));
            }
            finally
            {
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
                if (had && Directory.Exists(aside)) Directory.Move(aside, cache);
            }
        }

        /// <summary>Is the candidate measurably better than the baseline, and no worse anywhere that matters?</summary>
        public static bool Better(EngineRun baseline, EngineRun candidate)
        {
            if (candidate.Crashed || candidate.Frames <= 0) return false;
            if (candidate.Frames < baseline.Frames * 0.9) return false;
            bool fewerSlow = candidate.SlowFrames <= baseline.SlowFrames * 0.8 && candidate.SlowFrames < baseline.SlowFrames;
            bool shorterWorst = candidate.WorstMs <= baseline.WorstMs * 0.8;
            bool lessWaiting = baseline.WaitedMs > 50 && candidate.WaitedMs <= baseline.WaitedMs * 0.8;
            return (fewerSlow || shorterWorst || lessWaiting) && candidate.SlowFrames <= baseline.SlowFrames + 1;
        }

        public static string Improve(string game, IProgress<string> progress, CancellationToken token)
        {
            string ws = Workspace(), src = Path.Combine(ws ?? "", "src");
            if (!Available()) throw new IOException(Availability());
            game = Path.GetFullPath(game); GameOptimization.EnsureStopped(game);
            if (Git(src, "status --porcelain --untracked-files=no", token).Trim().Length > 0) throw new IOException("The engine source has unsaved changes. Commit or revert them first.");
            string package = Path.Combine(ws, "package"), candidate = Path.Combine(ws, "package-candidate");
            if (!File.Exists(Path.Combine(package, "scarletnx.exe")))
            {
                progress.Report("Building the current engine first (the first build takes a while).");
                MsysScript(ws, "build-engine.sh", null, 7200, token); MsysScript(ws, "package-engine.sh", package, 600, token);
            }
            progress.Report("Measuring the current engine on " + Path.GetFileNameWithoutExtension(game) + " (scripted play, empty shader cache).");
            var baseline = Measure(Path.Combine(package, "scarletnx.exe"), game, "Current engine", token);
            progress.Report(baseline.Summary);
            var health = GameDiagnostics.Scan(game, null, null);
            string head = Git(src, "rev-parse HEAD", token).Trim();
            string prompt =
                "You are improving the PortForge Engine, a fork of the ScarletNX runtime (C99, Vulkan renderer) that runs Switch games recompiled for Windows. " +
                "Goal: remove stutter (frames over 50 ms) in the game described below, for all games, without changing what is drawn for longer than a frame. " +
                "Read CLAUDE.md and the code first. Rules: change only files under src/nx/gpu/ and src/nx/runtime/settings.txt (and src/nx/runtime/present.c if frame pacing is the cause); " +
                "never change the generated-code ABI (src/common/cpu), never special-case a game or title; keep strict C99 and the project's style; put any new behaviour behind a setting in settings.txt that defaults on; " +
                "do not build or run anything (PortForge builds and measures your change); keep the change small and focused on the biggest measured cause. " +
                "If you find no safe improvement, change nothing. End with a short plain-language summary of what you changed and why.\n\n" +
                "MEASURED (scripted gameplay, empty shader cache): " + baseline.Summary + "\nDIAGNOSIS:\n" + GameDiagnostics.SafeEvidence(health);
            progress.Report("The AI is studying the engine and writing a change (this can take several minutes).");
            string answer = Path.Combine(Path.GetTempPath(), "portforge-lab-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                try { Run(CodexDiagnosis.Find(), "exec --skip-git-repo-check --sandbox workspace-write -c approval_policy=\"never\" -c web_search=\"disabled\" --output-last-message " + GameManifest.Quote(answer) + " -", src, 1800, token, prompt); }
                catch (IOException e)
                {
                    if (e.Message.IndexOf("usage limit", StringComparison.OrdinalIgnoreCase) >= 0) throw new IOException("Codex has reached its usage limit for now, so the AI could not work on the engine. Nothing was changed. Try again later.", e);
                    if (e.Message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || e.Message.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0) throw new IOException("Sign in to Codex on this PC to use the AI Engine Lab. Nothing was changed.", e);
                    throw;
                }
                string summary = File.Exists(answer) ? File.ReadAllText(answer).Trim() : "(no summary)";
                var changed = Git(src, "status --porcelain", token).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 3).Select(l => l.Substring(l.IndexOf(' ') + 1).Trim().Replace('\\', '/')).ToList();
                if (changed.Count == 0) return "The AI found no safe change to make this time.\r\nAI: " + summary;
                var outside = changed.Where(f => !AllowedPaths.Any(a => f.StartsWith(a, StringComparison.Ordinal))).ToList();
                if (outside.Count > 0) throw new IOException("The AI changed files outside the engine's renderer (" + String.Join(", ", outside) + "). The change was thrown away.");
                progress.Report("The AI changed " + String.Join(", ", changed) + ". Building the engine with the change.");
                MsysScript(ws, "build-engine.sh", null, 7200, token);
                MsysScript(ws, "package-engine.sh", candidate, 600, token);
                progress.Report("Measuring the changed engine the same way.");
                var trial = Measure(Path.Combine(candidate, "scarletnx.exe"), game, "AI-changed engine", token);
                progress.Report(trial.Summary);
                if (!Better(baseline, trial)) throw new LabRejected("The change did not measurably reduce stutter (or caused errors), so it was thrown away.\r\n" + baseline.Summary + "\r\n" + trial.Summary + "\r\nAI: " + summary);
                Git(src, "add -A src", token);
                Git(src, "-c user.name=PortForge -c user.email=portforge@localhost commit -q -m " + GameManifest.Quote("[engine-lab] " + FirstLine(summary)), token);
                string version = File.Exists(Path.Combine(ws, "VERSION")) ? File.ReadAllText(Path.Combine(ws, "VERSION")).Trim() : "1.0";
                File.WriteAllText(Path.Combine(ws, "VERSION"), version + "+lab" + DateTime.Now.ToString("MMddHHmm"));
                File.AppendAllText(Path.Combine(ws, "FEATURES"), "; " + FirstLine(summary));
                MsysScript(ws, "package-engine.sh", package, 600, token);
                CopyPackage(package, EngineManager.DefaultFolder);
                return "Kept: the AI's engine change measurably reduced stutter.\r\n" + baseline.Summary + "\r\n" + trial.Summary + "\r\nAI: " + summary + "\r\nSelect Install PortForge Engine to put the improved engine under this game.";
            }
            catch (Exception)
            {
                // Anything not kept is reverted, so the source stays at the last measured-good engine.
                try { Git(src, "reset -q --hard " + head, CancellationToken.None); Git(src, "clean -q -fd src", CancellationToken.None); } catch (Exception) { }
                throw;
            }
            finally { try { File.Delete(answer); } catch (IOException) { } try { if (Directory.Exists(candidate)) Directory.Delete(candidate, true); } catch (IOException) { } }
        }

        private static string FirstLine(string text) { string line = (text ?? "").Split('\n').Select(l => l.Trim().TrimStart('#', '*', '-', ' ')).FirstOrDefault(l => l.Length > 0) ?? "engine change"; return line.Length > 90 ? line.Substring(0, 90) : line; }

        public static void CopyPackage(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string old in Directory.GetFiles(to)) File.Delete(old);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(from)) { string target = Path.Combine(to, Path.GetFileName(dir)); Directory.CreateDirectory(target); foreach (string f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true); }
        }

        public sealed class LabRejected : IOException { public LabRejected(string message) : base(message) { } }
    }
}

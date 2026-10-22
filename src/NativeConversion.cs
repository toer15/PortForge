using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PortForge
{
    public sealed class NativeRequest
    {
        public string Name, Input, Backend, Output, ProductKeys, TitleKeys;
        public bool AutoOptimize;
        public string TuningWarning;
    }

    public static class NativeConversion
    {
        public const string BackendRelease = "nightly-2026-10-21-0de0a87";
        public const string BackendUrl = "https://github.com/scarletnx/ScarletNX";

        public static void Validate(NativeRequest r)
        {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Native Switch conversion requires 64-bit Windows.");
            if (String.IsNullOrWhiteSpace(r.Name) || r.Name != r.Name.Trim() || r.Name.Length > 64 || r.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || r.Name.EndsWith(".") ||
                Regex.IsMatch(r.Name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                throw new ArgumentException("Choose a game name of 1-64 characters that is a valid Windows filename.");
            if (!File.Exists(r.Backend) || !String.Equals(Path.GetFileName(r.Backend), "scarletnx.exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select scarletnx.exe from an extracted ScarletNX Windows release.");
            if (!File.Exists(r.Input) && !Directory.Exists(r.Input)) throw new ArgumentException("Choose a Switch game file or an extracted game folder.");
            if (String.IsNullOrWhiteSpace(r.Output) || !Path.IsPathRooted(r.Output)) throw new ArgumentException("Choose an absolute output folder.");
            foreach (string path in new[] { r.Input, r.Backend, r.Output }) Paths.CheckLinks(path);
            foreach (string key in new[] { r.ProductKeys, r.TitleKeys })
                if (!String.IsNullOrWhiteSpace(key) && !File.Exists(key)) throw new ArgumentException("The selected key file does not exist.");
            string destination = Path.Combine(Path.GetFullPath(r.Output), r.Name);
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("That output already exists. Choose a new name or output folder.");
            if (Paths.Overlap(destination, Path.GetDirectoryName(Path.GetFullPath(r.Backend)))) throw new IOException("Choose an output outside the backend folder.");
            if (Directory.Exists(r.Input))
            {
                if (Paths.Overlap(destination, r.Input)) throw new IOException("Choose an output outside the input folder.");
                string exefs = Directory.Exists(Path.Combine(r.Input, "exefs")) ? Path.Combine(r.Input, "exefs") : r.Input;
                if (!File.Exists(Path.Combine(exefs, "main"))) throw new ArgumentException("An extracted game folder must contain exefs/main (or main directly).");
            }
            else
            {
                string kind = DetectInput(r.Input);
                if (kind == null) throw new InvalidDataException("Unsupported input header. Use an uncompressed NRO, NSP/PFS0 container or XCI/HEAD container, or an extracted game folder.");
            }
        }

        public static string DetectInput(string file)
        {
            byte[] header = new byte[0x104];
            using (var stream = File.OpenRead(file))
            {
                int count = stream.Read(header, 0, header.Length);
                if (count >= 0x80 && Encoding.ASCII.GetString(header, 0x10, 4) == "NRO0") return "NRO";
                if (count >= 0x10 && Encoding.ASCII.GetString(header, 0, 4) == "PFS0") return "NSP";
                if (count >= header.Length && Encoding.ASCII.GetString(header, 0x100, 4) == "HEAD") return "XCI";
            }
            return null;
        }

        public static string Convert(NativeRequest r, IProgress<string> progress, CancellationToken token, IProgress<ConversionStatus> status = null)
        {
            r.TuningWarning = null;
            ReportStatus(status, "Checking your choices", "Checking the game file, conversion engine and save folder.");
            Validate(r);
            token.ThrowIfCancellationRequested();
            string output = Path.GetFullPath(r.Output);
            Directory.CreateDirectory(output);
            string id = Guid.NewGuid().ToString("N");
            string stage = Path.Combine(output, ".portforge-native-" + id);
            string logPath = Path.Combine(output, r.Name + "-build-" + id.Substring(0, 8) + ".log");
            Directory.CreateDirectory(stage);
            string compiledGame = null;
            bool compiledComplete = false, published = false;
            try
            {
                using (var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    log.WriteLine("PortForge native conversion | " + DateTime.UtcNow.ToString("u"));
                    log.WriteLine("Backend: " + r.Backend);
                    log.WriteLine("Input: " + r.Input);
                    log.WriteLine("AOT compilation is experimental; commercial gameplay and zero JIT fallback are not guaranteed.");
                    progress.Report("Build log: " + logPath);
                    string input = Path.GetFullPath(r.Input);
                    string repo = Path.Combine(stage, "backend-state");
                    Directory.CreateDirectory(repo);
                    if (File.Exists(input) && DetectInput(input) != "NRO")
                    {
                        ReportStatus(status, "Reading the game", "Preparing the game files. Large games can take a while.");
                        string extracted = Path.Combine(stage, "extracted");
                        var extract = new List<string> { "--repo", repo, "-x", input, "--out", extracted };
                        if (!String.IsNullOrWhiteSpace(r.ProductKeys)) { extract.Add("--keys"); extract.Add(Path.GetFullPath(r.ProductKeys)); }
                        if (!String.IsNullOrWhiteSpace(r.TitleKeys)) { extract.Add("--title-keys"); extract.Add(Path.GetFullPath(r.TitleKeys)); }
                        Run(r.Backend, extract, stage, log, progress, token);
                        if (!Directory.Exists(extracted)) throw new IOException("The backend did not create the extracted game folder.");
                        input = extracted;
                    }
                    string compiled = Path.Combine(stage, "compiled");
                    ReportStatus(status, "Converting the game", "Creating Windows game code. You can follow the details below or cancel at any time.");
                    Run(r.Backend, new[] { "--repo", repo, "-r", input, r.Name, "--outdir", compiled, "--runtime", Path.GetFullPath(r.Backend), "--jobs", "2", "--keep-ir" }, stage, log, progress, token);
                    ReportStatus(status, "Finishing up", "Checking the result and saving the files to your game folder.");
                    string game = Path.Combine(compiled, r.Name);
                    compiledGame = game;
                    string exe = Path.Combine(game, r.Name + ".exe");
                    string module = Path.Combine(game, r.Name + "_code.dll");
                    RequireNativeX64(exe);
                    RequireNativeX64(module);
                    if (!File.Exists(Path.Combine(game, r.Name + ".abi"))) throw new IOException("The backend did not publish a completed compilation checkpoint.");
                    compiledComplete = true;
                    File.WriteAllText(Path.Combine(game, "PORTFORGE.txt"),
                        "Compiled by ScarletNX, orchestrated by PortForge.\r\n" + BackendUrl + "\r\n\r\n" +
                        "Run " + r.Name + ".exe. Keep this entire folder together.\r\n" +
                        "The _code.dll contains AOT-compiled game code. The .exe supplies the runtime.\r\n" +
                        "The backend also supports JIT fallback for undiscovered code. This build is not certified as fully AOT or playable.\r\n" +
                        "Graphics, audio, saves, controllers and service compatibility depend on this backend and game.\r\n" +
                        "Game assets are copied from the selected input. No keys are intentionally included.\r\n" +
                        "The backend targets the build PC's CPU features; moving to an older CPU is not guaranteed.\r\n");
                    if (r.AutoOptimize)
                    {
                        ReportStatus(status, "Tuning graphics", "Comparing four startup profiles. This takes about two minutes. Gameplay performance can vary.");
                        try
                        {
                            var tuneProgress = new InlineProgress(line => { log.WriteLine(line); progress.Report(line); ReportStatus(status, "Tuning graphics", line); });
                            var tuned = GameOptimization.Tune(exe, GameOptimization.Load(exe), 60, tuneProgress, token);
                            GameOptimization.Save(exe, tuned.Profile, RuntimeCapabilities.Read(exe, token));
                            log.WriteLine(tuned.Summary.Replace("Select Save settings to apply.", "Applied to the generated game."));
                            progress.Report("Automatic tuning complete. Measurements are in the game's PortForge-performance report.");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception tuningError)
                        {
                            string warning = "Automatic tuning could not finish; the converted game will still be saved. " + tuningError.Message;
                            r.TuningWarning = warning;
                            log.WriteLine("WARNING: " + warning); progress.Report(warning);
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    string destination = Path.Combine(output, r.Name);
                    ConversionFiles.Publish(game, destination, token);
                    published = true;
                    try { log.WriteLine("Compilation complete. Gameplay unverified. Output: " + destination); } catch { }
                    return destination;
                }
            }
            catch (Exception error)
            {
                try { File.AppendAllText(logPath, "\r\nStopped: " + error + "\r\n"); } catch { }
                if (error is OperationCanceledException) throw;
                var failure = error as ConversionFailure;
                if (failure != null) { failure.LogPath = logPath; throw; }
                if (compiledComplete && !published)
                    throw new ConversionFailure("Could not save the finished game", "The game code was compiled, but saving the final folder failed. The completed work has been kept. Open saved work to inspect it, or use a different save folder.", null, error.ToString())
                    { LogPath = logPath, RecoveryFolder = compiledGame };
                throw new IOException(error.Message + "\r\nBuild log: " + logPath, error);
            }
            finally
            {
                if (compiledComplete && !published)
                {
                    SafeWarning(progress, logPath, "Saved completed compiler output for recovery: " + compiledGame);
                }
                else
                {
                    ConversionFiles.TryCleanup(stage, output, message => SafeWarning(progress, logPath, message));
                }
            }
        }

        private static void SafeWarning(IProgress<string> progress, string logPath, string message)
        {
            try { File.AppendAllText(logPath, Environment.NewLine + message + Environment.NewLine); } catch { }
            try { progress.Report(message); } catch { }
        }

        private sealed class InlineProgress : IProgress<string>
        {
            private readonly Action<string> action;
            public InlineProgress(Action<string> action) { this.action = action; }
            public void Report(string value) { action(value); }
        }

        private static void ReportStatus(IProgress<ConversionStatus> status, string title, string message)
        {
            if (status != null) status.Report(new ConversionStatus(title, message));
        }

        private static void Run(string backend, IEnumerable<string> arguments, string cwd, StreamWriter log, IProgress<string> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(backend, String.Join(" ", arguments.Select(GameManifest.Quote)))
            {
                WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = start })
            {
                var recent = new Queue<string>();
                DataReceivedEventHandler received = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (log)
                    {
                        log.WriteLine(e.Data);
                        recent.Enqueue(e.Data);
                        while (recent.Count > 30) recent.Dequeue();
                    }
                    progress.Report(e.Data);
                };
                process.OutputDataReceived += received;
                process.ErrorDataReceived += received;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    while (!process.WaitForExit(150)) token.ThrowIfCancellationRequested();
                    process.WaitForExit(); // Flush both asynchronous streams.
                    token.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0)
                    {
                        string details;
                        lock (log) details = String.Join(Environment.NewLine, recent);
                        throw ConversionFailure.FromBackend(process.ExitCode, details);
                    }
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        using (var kill = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                            if (kill != null) kill.WaitForExit(5000);
                        if (!process.HasExited) process.Kill();
                        process.WaitForExit();
                    }
                }
            }
        }

        public static void RequireNativeX64(string file)
        {
            if (!File.Exists(file)) throw new IOException("The backend did not generate " + Path.GetFileName(file));
            using (var reader = new BinaryReader(File.OpenRead(file)))
            {
                if (reader.BaseStream.Length < 512 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Invalid Windows executable: " + file);
                reader.BaseStream.Position = 0x3c;
                uint pe = reader.ReadUInt32();
                if (pe > reader.BaseStream.Length - 26) throw new InvalidDataException("Invalid PE header: " + file);
                reader.BaseStream.Position = pe;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("The output is not an x64 Windows binary: " + file);
            }
        }

    }
}

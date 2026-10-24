using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using PortForge;

internal static class NativeTests
{
    private static int checks;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);
    private sealed class Reporter : IProgress<string>
    {
        public Action<string> Callback;
        public void Report(string value) { Console.WriteLine(value); if (Callback != null) Callback(value); }
    }
    private sealed class StageReporter : IProgress<ConversionStatus>
    {
        public readonly System.Collections.Generic.List<string> Titles = new System.Collections.Generic.List<string>();
        public void Report(ConversionStatus value) { Titles.Add(value.Title); }
    }
    private static void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); checks++; Console.WriteLine("PASS: " + text); }
    private static void Reject(Action action, string text) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, text); }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || !File.Exists(args[0]) || !File.Exists(args[1])) throw new IOException("Native integration tests need the real ScarletNX backend and original-test.nro fixture.");
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string input = Path.Combine(root, "Switch code with spaces.nro");
            File.Copy(args[1], input);
            var request = new NativeRequest { Name = "Compiled Switch Test", Input = input, Backend = Path.GetFullPath(args[0]), Output = Path.Combine(root, "exports"), MatchHardware = false };
            Check(NativeConversion.DetectInput(input) == "NRO", "Switch NRO detected from its header");
            var stages = new StageReporter();
            string built = NativeConversion.Convert(request, new Reporter(), CancellationToken.None, stages);
            Check(stages.Titles.SequenceEqual(new[] { "Checking your choices", "Converting the game", "Finishing up" }), "User-facing progress follows the real conversion stages");
            NativeConversion.RequireNativeX64(Path.Combine(built, request.Name + ".exe"));
            NativeConversion.RequireNativeX64(Path.Combine(built, request.Name + "_code.dll"));
            Check(true, "Real backend generated x64 PE executable and game-code DLL");
            Check(Directory.GetFiles(built, "*.ll", SearchOption.AllDirectories).Length == 0, "The finished game folder has no per-function LLVM IR (0.48 stopped writing it: it only cost disk time)");
            Check(File.Exists(Path.Combine(built, request.Name + "_data", "exefs", "main.nro")), "Required guest image staged inside the output");
            Reject(() => NativeConversion.Convert(request, new Reporter(), CancellationToken.None), "Existing native output is preserved");
            var autoRequest = new NativeRequest { Name = "Automatic tuning fallback", Input = input, Backend = request.Backend, Output = request.Output, AutoOptimize = true };
            string autoBuilt = NativeConversion.Convert(autoRequest, new Reporter(), CancellationToken.None);
            Check(File.Exists(Path.Combine(autoBuilt, autoRequest.Name + ".exe")), "A game without a usable graphics benchmark is still published when automatic tuning is enabled");
            string autoLog = Directory.GetFiles(request.Output, autoRequest.Name + "-build-*.log").Single();
            Check(File.ReadAllText(autoLog).Contains("Automatic tuning could not finish"), "Unsupported automatic tuning is recorded as a warning instead of a conversion failure");
            Check(!String.IsNullOrWhiteSpace(autoRequest.TuningWarning), "Skipped tuning is available to show prominently in the completion status");
            Check(File.Exists(Path.Combine(autoBuilt, PcOptimization.ReportName)) && !String.IsNullOrWhiteSpace(autoRequest.PcSummary), "Conversion automatically writes a report of this PC and the settings applied even when measurement cannot finish");
            File.Delete(input);
            string moved = Path.Combine(root, "Moved native game & spaces");
            Directory.Move(built, moved);
            var start = new ProcessStartInfo(Path.Combine(moved, request.Name + ".exe"), "-H --log=2") { WorkingDirectory = moved, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(); throw new Exception("Runtime timed out."); }
                string text = stdout.Result + stderr.Result;
                File.WriteAllText(Path.Combine(root, "native-run.txt"), text);
                Check(process.ExitCode == 0 && text.Contains("[game] PortForge native AArch64 test"), "Moved executable runs the original AArch64 test after deleting the input file");
                Check(text.Contains("jit: 0 batches compiled 0 pcs"), "Test reports zero JIT-compiled instructions");
            }
            Check(!Directory.GetDirectories(request.Output, ".portforge-native-*").Any(), "Completed conversion removes temporary staging files");
            Check(Directory.GetFiles(request.Output, request.Name + "-build-*.log").Length == 1, "Build log survives publication");

            string broken = Path.Combine(root, "broken.nro");
            byte[] header = new byte[512];
            System.Text.Encoding.ASCII.GetBytes("NRO0").CopyTo(header, 0x10);
            File.WriteAllBytes(broken, header);
            request.Input = broken;
            request.Name = "Bad image";
            Reject(() => NativeConversion.Convert(request, new Reporter(), CancellationToken.None), "Real backend failure is reported");
            Check(!Directory.Exists(Path.Combine(request.Output, request.Name)) && !Directory.GetDirectories(request.Output, ".portforge-native-*").Any(), "Backend failure leaves no completed game or temporary staging folder");

            request.Input = args[1];
            request.Name = "Cancelled compile";
            var cancellation = new CancellationTokenSource();
            var reporter = new Reporter { Callback = line => { if (line.StartsWith("Recompiling")) cancellation.Cancel(); } };
            Reject(() => NativeConversion.Convert(request, reporter, cancellation.Token), "Cancellation during a real compiler run is handled");
            Check(!Directory.Exists(Path.Combine(request.Output, request.Name)) && !Directory.GetDirectories(request.Output, ".portforge-native-*").Any(), "Cancelled conversion publishes no game and removes temporary files");

            string encrypted = Path.Combine(root, "key-error.nsp");
            var container = new byte[16];
            System.Text.Encoding.ASCII.GetBytes("PFS0").CopyTo(container, 0);
            File.WriteAllBytes(encrypted, container);
            request.Input = encrypted;
            request.Backend = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "key-error-backend", "scarletnx.exe");
            request.Name = "Needs keys";
            ConversionFailure keyError = null;
            try { NativeConversion.Convert(request, new Reporter(), CancellationToken.None); }
            catch (ConversionFailure error) { keyError = error; }
            Check(keyError != null && keyError.UserTitle == "Key file needed" && keyError.NextStep.Contains("prod.keys"), "Missing-key engine diagnostic becomes an actionable user message");
            Check(keyError.Field == "keys" && File.Exists(keyError.LogPath), "Missing-key error identifies the correct input and keeps its full log");
            Check(!Directory.Exists(Path.Combine(request.Output, request.Name)), "Missing-key failure is never published as a completed game");

            string deepRoot = Path.Combine(root, "long-output-" + new String('a', 60), new String('b', 60));
            string deepWork = Path.Combine(deepRoot, ".portforge-native-" + Guid.NewGuid().ToString("N"));
            string compiledGame = Path.Combine(deepWork, "compiled", "Deep Game");
            string assetRelative = Path.Combine("Deep Game_data", "romfs", "SystemData", "EffectDataBase.szs");
            string asset = Path.Combine(compiledGame, assetRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            File.WriteAllText(asset, "original synthetic asset bytes");
            Directory.CreateDirectory(Path.Combine(compiledGame, "empty-assets"));
            string originalModule = Path.Combine(moved, "Compiled Switch Test_code.dll");
            byte[] originalBytes = File.ReadAllBytes(originalModule);
            Check(CreateHardLink(Path.Combine(compiledGame, "linked-runtime.dll"), originalModule, IntPtr.Zero), "Publishing fixture shares an actual DLL through a hard link");
            string deepDestination = Path.Combine(deepRoot, "Deep Game");
            ConversionFiles.Publish(compiledGame, deepDestination, CancellationToken.None);
            string finalAsset = Path.Combine(deepDestination, assetRelative);
            Check(finalAsset.Length > 260 && File.ReadAllText(finalAsset) == "original synthetic asset bytes", "Asset with a path over 260 characters is published without changing its contents");
            Check(Directory.Exists(Path.Combine(deepDestination, "empty-assets")), "Publishing preserves empty asset directories");
            File.WriteAllText(Path.Combine(deepDestination, "linked-runtime.dll"), "independent output");
            Check(File.ReadAllBytes(originalModule).SequenceEqual(originalBytes), "Published DLL changes cannot modify the original hard-linked library");
            string retryTarget = Path.Combine(root,"briefly-locked.dll"), retryCopy = Path.Combine(root,"replacement.tmp");
            File.WriteAllText(retryTarget,"before"); File.WriteAllText(retryCopy,"after");
            using (var briefLock = new FileStream(retryTarget,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var replacing = System.Threading.Tasks.Task.Run(() => ConversionFiles.ReplaceWithRetry(retryCopy,retryTarget,CancellationToken.None));
                Thread.Sleep(150); Check(!replacing.IsCompleted,"Temporary publication file locks are retried instead of failing immediately");
                briefLock.Dispose(); replacing.GetAwaiter().GetResult();
            }
            Check(File.ReadAllText(retryTarget)=="after","Publication recovers when the temporary lock is released");
            File.WriteAllText(retryCopy,"cancelled copy");
            using (var briefLock = new FileStream(retryTarget,FileMode.Open,FileAccess.Read,FileShare.Read))
            using (var stopRetry = new CancellationTokenSource(100))
            {
                bool stopped=false; try { ConversionFiles.ReplaceWithRetry(retryCopy,retryTarget,stopRetry.Token); } catch(OperationCanceledException) { stopped=true; }
                Check(stopped && File.ReadAllText(retryTarget)=="after","Cancelling a locked-file retry preserves the existing destination");
            }
            string leftover = Path.Combine(deepWork, new String('c', 80), "leftover.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(leftover));
            File.WriteAllText(leftover, "temporary synthetic file");
            bool cleanupWarned = false;
            Check(ConversionFiles.TryCleanup(deepWork, deepRoot, message => cleanupWarned = true) && !Directory.Exists(deepWork) && !cleanupWarned, "Cleanup handles nested paths over 260 characters");

            request.Input = broken;
            request.Backend = Path.GetFullPath(args[0]);
            request.Name = "Original failure survives cleanup";
            FileStream locked = null;
            var lockingProgress = new Reporter { Callback = line =>
            {
                if (locked == null && line.StartsWith("Build log: "))
                {
                    string work = Directory.GetDirectories(request.Output, ".portforge-native-*").Single();
                    locked = new FileStream(Path.Combine(work, "locked-for-test.txt"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
            } };
            Exception primaryError = null;
            try { NativeConversion.Convert(request, lockingProgress, CancellationToken.None); }
            catch (Exception error) { primaryError = error; }
            finally { if (locked != null) locked.Dispose(); }
            Check(primaryError is ConversionFailure && primaryError.Message.Contains("image size"), "A cleanup failure cannot replace the original compiler error");
            Check(File.ReadAllText(((ConversionFailure)primaryError).LogPath).Contains("WARNING: Temporary files could not be removed"), "Cleanup warnings are preserved separately in the full log");
            Console.WriteLine(checks + " native checks passed. Evidence: " + root);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

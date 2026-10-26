using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PortForge;

// Compiled ONLY with GameOptimization.cs by run_process_lookup_tests.py. No frontend rebuild.
internal static class ProcessLookupTests
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); checks++; Console.WriteLine("PASS: " + message); }
    private static void Blocked(Action action, string message)
    {
        bool blocked = false;
        try { action(); } catch (IOException) { blocked = true; }
        Check(blocked, message);
    }
    private static Process Start(string exe, string arguments)
    {
        return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe), RedirectStandardOutput = true, RedirectStandardError = true });
    }
    private static void Stop(Process process)
    {
        if (!process.HasExited) process.Kill();
        process.WaitForExit();
    }
    private static void IsRunning(string exe, int id, string message)
    {
        using (var found = GameOptimization.FindRunningGame(exe)) Check(found != null && found.Id == id, message);
    }
    private static string ImagePath(int id)
    {
        try { return (string)typeof(GameOptimization).GetMethod("RunningProcessImagePath", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { id }); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static void WaitForProbe(Task task)
    {
        var gate = typeof(GameOptimization).GetField("processGate", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var probes = (HashSet<int>)typeof(GameOptimization).GetField("probes", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            lock (gate)
            {
                foreach (int id in probes) using (var process = Process.GetProcessById(id)) if (!process.HasExited) return;
            }
            if (task.IsCompleted) throw new Exception("Probe completed before its live registration was observed.");
            Thread.Sleep(1);
        }
        throw new TimeoutException("Probe was not registered.");
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected fixture.exe and isolated output root.");
            string a = Path.Combine(args[1], "selected game \u00e6", "TuningFixture.exe"), b = Path.Combine(args[1], "other copy", "TuningFixture.exe");
            foreach (string exe in new[] { a, b }) { Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.Copy(args[0], exe, true); }
            using (var other = Start(b, ""))
            {
                try
                {
#if PROCESS_LOOKUP_NATIVE_FAULTS
                    ProcessLookupNativeFaults.Mode = "open-denied";
                    Blocked(() => GameOptimization.EnsureStopped(a), "Unidentifiable live same-name process blocks settings even in a different folder");
                    ProcessLookupNativeFaults.Mode = "query-denied";
                    Blocked(() => GameOptimization.EnsureStopped(a), "Failed image query for a live same-name process is not treated as stopped");
                    Check(ProcessLookupNativeFaults.Closed > 0, "A failed native image query still closes its handle");
                    ProcessLookupNativeFaults.Mode = "status-denied";
                    Blocked(() => GameOptimization.EnsureStopped(a), "Failed native liveness check stays closed");
                    ProcessLookupNativeFaults.Mode = "query-status-denied";
                    Blocked(() => GameOptimization.EnsureStopped(a), "Failed query with uncertain exit status stays closed");
                    ProcessLookupNativeFaults.Mode = "empty-image";
                    Blocked(() => GameOptimization.EnsureStopped(a), "Unexpected empty image path stays closed");
                    ProcessLookupNativeFaults.Mode = "query-exited";
                    Check(ImagePath(other.Id) == null, "Confirmed exit between status and failed image query is ignored");
                    ProcessLookupNativeFaults.Mode = "open-missing";
                    Check(ImagePath(other.Id) == null, "PID disappearing before OpenProcess is ignored");
                    ProcessLookupNativeFaults.Mode = "";
                    IsRunning(b, other.Id, "Native fault injection resets to the real image lookup");
#else
                    Check(ImagePath(other.Id) == b, "Limited native query reads the exact live fixture image path");
                    IsRunning(b, other.Id, "Live fixture matches its actual path");
                    using (var absent = GameOptimization.FindRunningGame(a)) Check(absent == null, "Other folder with the same basename does not match selected game");
                    GameOptimization.EnsureStopped(a);
                    Check(true, "Same basename in another folder does not block selected game settings");
                    using (var actual = Start(a, ""))
                    {
                        try
                        {
                            IsRunning(a, actual.Id, "Two live copies with the same basename are distinguished by full path");
                            IsRunning(a.ToUpperInvariant(), actual.Id, "Exact path matching remains case-insensitive on Windows");
                            Blocked(() => GameOptimization.EnsureStopped(a), "The selected live game blocks protected settings writes");
                            for (int i = 0; i < 100; i++) using (var found = GameOptimization.FindRunningGame(a)) if (found == null || found.Id != actual.Id) throw new Exception("Repeated lookup lost live fixture.");
                            Check(true, "100 repeated lookups retain the live path without ProcessModule access");
                        }
                        finally { Stop(actual); }
                        Check(ImagePath(actual.Id) == null, "Exited process is ignored even while its Process handle is retained");
                    }
                    using (var absent = GameOptimization.FindRunningGame(a)) Check(absent == null, "Exited selected game does not match the still-live other copy");
                    Check(!other.HasExited, "Only owned selected fixture was stopped; other fixture remains live");
#endif
                }
                finally { Stop(other); }
            }
#if !PROCESS_LOOKUP_NATIVE_FAULTS
            Check(ImagePath(Int32.MaxValue) == null, "Nonexistent PID is treated as exited");
            using (var untracked = Start(a, "--settings --slow-probe"))
            {
                try { IsRunning(a, untracked.Id, "An unregistered settings process remains conservatively treated as running"); }
                finally { Stop(untracked); }
            }
            var probe = Task.Run(() => GameOptimization.Run(a, "--settings --slow-probe", 10, CancellationToken.None));
            try
            {
                WaitForProbe(probe);
                using (var found = GameOptimization.FindRunningGame(a)) Check(found == null && !probe.IsCompleted, "A registered live settings probe is not mistaken for gameplay");
                GameOptimization.EnsureStopped(a);
                Check(true, "Registered probe does not block settings checks");
            }
            finally { probe.GetAwaiter().GetResult(); }
            Check(probe.Result.Contains("fps_limit"), "Probe output and normal completion remain intact");
            using (var actual = Start(a, ""))
            {
                try { IsRunning(a, actual.Id, "Probe cleanup does not hide a subsequently launched player process"); }
                finally { Stop(actual); }
            }
            GameOptimization.EnsureStopped(a);
            Check(true, "All owned fixture exits leave selected game stopped");
#endif
            Console.WriteLine("PASS ProcessLookupTests: " + checks + " checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

#if PROCESS_LOOKUP_NATIVE_FAULTS
// Test-only call-site substitution: exercise fail-closed/race branches without ACL changes,
// elevation, protected processes, or any production injection seam.
internal static class ProcessLookupNativeFaults
{
    internal static string Mode = "";
    internal static int Closed;
    private static int statusChecks;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", EntryPoint = "SetLastError", SetLastError = true)] private static extern void SetError(uint error);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal static IntPtr Open(uint access, bool inherit, int id)
    {
        if (access != 0x1000 || inherit) throw new Exception("Image lookup requested more than non-inherited limited query access.");
        statusChecks = 0;
        if (Mode == "open-denied" || Mode == "open-missing") { SetError(Mode == "open-denied" ? 5u : 87u); return IntPtr.Zero; }
        return OpenProcess(access, inherit, id);
    }
    internal static bool Query(IntPtr handle, uint flags, System.Text.StringBuilder path, ref int size)
    {
        if (flags != 0 || size != path.Capacity || size < 32768) throw new Exception("Image query lost Win32 path format or long-path capacity.");
        if (Mode == "empty-image") { size = 0; return true; }
        if (Mode.StartsWith("query-")) { SetError(5); return false; }
        return QueryFullProcessImageName(handle, flags, path, ref size);
    }
    internal static bool Status(IntPtr handle, out uint code)
    {
        statusChecks++;
        if (Mode == "status-denied" || (Mode == "query-status-denied" && statusChecks > 1)) { code = 0; SetError(5); return false; }
        if (Mode == "query-exited") { code = statusChecks == 1 ? 259u : 0u; return true; }
        return GetExitCodeProcess(handle, out code);
    }
    internal static bool Close(IntPtr handle) { Closed++; return CloseHandle(handle); }
}
#endif

// Compile-only dependencies for the complete production GameOptimization.cs file.
// These methods are never part of the frontend and no test relies on their behavior.
namespace PortForge
{
    internal static class NativeConversion { internal static void RequireNativeX64(string path) { throw new NotSupportedException("Not a process lookup operation"); } }
    internal static class ConversionFiles { internal static void ReplaceWithRetry(string a, string b, CancellationToken c, string d) { throw new NotSupportedException(); } }
    internal static class ControllerSettings { internal static void AddTo(Dictionary<string, string> a, RuntimeCapabilities b, Dictionary<string, string> c) { throw new NotSupportedException(); } }
    internal sealed class GameStateGuard : IDisposable { internal GameStateGuard(string path) { throw new NotSupportedException(); } public void Dispose() { } }
    internal static class PlayFirst { internal static bool Holding(string path) { throw new NotSupportedException(); } internal static object Track(string path, Process process) { throw new NotSupportedException(); } internal static bool Release(object tracked) { throw new NotSupportedException(); } }
    internal sealed class LookupStall { internal int FrameMs { get { throw new NotSupportedException(); } } }
    internal static class GameDiagnostics { internal static List<LookupStall> StallFrames(string log) { throw new NotSupportedException(); } }
}

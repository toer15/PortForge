using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using PortForge;

internal static class Tests
{
    private static int passed;
    private static string testRoot;
    private sealed class Reporter : IProgress<string>
    {
        public Action<string> Callback;
        public void Report(string value) { if (Callback != null) Callback(value); }
    }
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
        passed++;
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch { rejected = true; }
        Assert(rejected, message);
    }
    private static void RunLauncher(string exe)
    {
        using (var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }))
        {
            if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Launcher timeout"); }
            Assert(process.ExitCode == 23, "Launcher returns the runtime's exit code");
        }
    }
    private static int Main(string[] args)
    {
        testRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            string runtimeFolder = Path.Combine(testRoot, "Input runtime");
            string gameFolder = Path.Combine(testRoot, "Input games");
            Directory.CreateDirectory(runtimeFolder);
            Directory.CreateDirectory(gameFolder);
            Directory.CreateDirectory(Path.Combine(runtimeFolder, "plugins"));
            Directory.CreateDirectory(Path.Combine(runtimeFolder, "empty-config"));
            Directory.CreateDirectory(Path.Combine(gameFolder, "empty-assets"));
            File.Copy(args[1], Path.Combine(runtimeFolder, "Runtime.exe"));
            File.WriteAllText(Path.Combine(runtimeFolder, "plugins", "dependency.txt"), "dependency");
            string game = Path.Combine(gameFolder, "Spaced & unicode - æøå {runtime}.rom");
            File.WriteAllText(game, "user-owned test data");
            var request = new BuildRequest
            {
                Title = "Game with spaces", Profile = "Test", RuntimeExe = Path.Combine(runtimeFolder, "Runtime.exe"),
                GamePath = game, OutputFolder = Path.Combine(testRoot, "Exports"), Portable = true,
                Arguments = new[] { "{game}", "--literal", "a\"b", "ends with slash\\", "", "{package}", "{runtime}", "{gameDir}" }
            };
            byte[] launcher = File.ReadAllBytes(args[0]);
            var plan = PackageBuilder.Inspect(request, CancellationToken.None);
            Assert(plan.Files == 3, "Preflight includes the entire runtime directory and game file");
            string built = PackageBuilder.Build(request, launcher, new Reporter(), CancellationToken.None);
            Assert(File.Exists(Path.Combine(built, "runtime", "plugins", "dependency.txt")), "Runtime dependencies copied");
            Assert(Directory.Exists(Path.Combine(built, "runtime", "empty-config")), "Empty runtime directories preserved");
            Assert(File.ReadAllText(Path.Combine(built, "game", Path.GetFileName(game))) == "user-owned test data", "Game file copied unchanged");
            string moved = Path.Combine(testRoot, "Moved export - æøå & spaces");
            Directory.Move(built, moved);
            RunLauncher(Path.Combine(moved, request.Title + ".exe"));
            var proof = new XmlDocument();
            proof.Load(Path.Combine(moved, "runtime", "proof.xml"));
            string[] received = proof.SelectNodes("/Proof/Argument").Cast<XmlNode>().Select(n => n.InnerText).ToArray();
            Assert(received.SequenceEqual(new[] { Path.Combine(moved, "game", Path.GetFileName(game)), "--literal", "a\"b", "ends with slash\\", "", moved + Path.DirectorySeparatorChar, Path.Combine(moved, "runtime"), Path.Combine(moved, "game") }), "Moved export launches with correct Unicode, spaces, quotes, empty arguments and backslashes");
            Assert(proof.SelectSingleNode("/Proof/WorkingDirectory").InnerText == Path.Combine(moved, "runtime"), "Runtime working directory set correctly");

            var manifest = GameManifest.Load(Path.Combine(moved, "portforge.xml"));
            manifest.Runtime = "..\\outside.exe";
            Reject(() => manifest.Resolve(moved), "Portable manifest rejects path traversal");
            manifest.Runtime = request.RuntimeExe;
            Reject(() => manifest.Resolve(moved), "Portable manifest rejects absolute runtime paths");
            manifest.Runtime = "runtime\\missing.exe";
            Reject(() => manifest.Resolve(moved), "Missing runtime produces a useful failure");
            string invalidXml = Path.Combine(testRoot, "bad.xml");
            File.WriteAllText(invalidXml, "<!DOCTYPE PortForge [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><PortForge><Title>&xxe;</Title></PortForge>");
            Reject(() => GameManifest.Load(invalidXml), "Package XML rejects external entities");

            request.Title = "Linked Game";
            request.Portable = false;
            built = PackageBuilder.Build(request, launcher, new Reporter(), CancellationToken.None);
            Assert(!Directory.Exists(Path.Combine(built, "runtime")), "Linked export does not copy game or runtime files");
            RunLauncher(Path.Combine(built, request.Title + ".exe"));
            Assert(File.Exists(Path.Combine(runtimeFolder, "proof.xml")), "Linked export launches the original runtime");
            Reject(() => PackageBuilder.Build(request, launcher, new Reporter(), CancellationToken.None), "Existing exports cannot be overwritten");
            Assert(File.Exists(Path.Combine(built, request.Title + ".exe")), "Existing export survives rejected overwrite");

            request.Portable = true;
            request.Title = "Self copy";
            request.OutputFolder = Path.Combine(runtimeFolder, "exports");
            Reject(() => PackageBuilder.Inspect(request, CancellationToken.None), "Export inside runtime folder rejected");
            request.OutputFolder = Path.Combine(gameFolder, "exports");
            Reject(() => PackageBuilder.Inspect(request, CancellationToken.None), "Export inside game folder rejected");
            request.OutputFolder = Path.Combine(testRoot, "Exports");
            request.Title = "CON";
            Reject(() => PackageBuilder.Validate(request), "Windows reserved filenames rejected");
            request.Title = "Bad args";
            request.Arguments = new[] { "{unknown}", "{game}" };
            Reject(() => PackageBuilder.Validate(request), "Unknown argument placeholders rejected");
            request.Arguments = new[] { "--no-game" };
            Reject(() => PackageBuilder.Validate(request), "Accidentally omitted game argument rejected");

            request.Arguments = new[] { "{game}" };
            request.Title = "Cancelled Game";
            var cancelled = new CancellationTokenSource();
            var progress = new Reporter { Callback = line => { if (line.StartsWith("Copying")) cancelled.Cancel(); } };
            Reject(() => PackageBuilder.Build(request, launcher, progress, cancelled.Token), "Cancellation interrupts copying");
            Assert(!Directory.Exists(Path.Combine(request.OutputFolder, request.Title)), "Cancelled export never appears as complete");
            Assert(Directory.GetDirectories(request.OutputFolder, ".portforge-*").Length == 0, "Cancellation cleans only its staging folder");

            request.Title = "Folder game";
            request.GamePath = gameFolder;
            built = PackageBuilder.Build(request, launcher, new Reporter(), CancellationToken.None);
            Assert(Directory.Exists(GameManifest.Load(Path.Combine(built, "portforge.xml")).Resolve(built).Arguments.Trim('"')), "Game-directory input resolves to an existing exported directory");
            Assert(Directory.Exists(Path.Combine(built, "game", "empty-assets")), "Empty game directories preserved");
            Console.WriteLine("\n" + passed + " checks passed. Test files: " + testRoot);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine("Test files: " + testRoot); return 1; }
    }
}

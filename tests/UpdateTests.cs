using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using PortForge;

internal sealed class ReleaseServer : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>();
    public string BaseUrl;

    public ReleaseServer(string exePath)
    {
        listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        assets["good"] = Zip(z => { Add(z, "PortForge-9/PortForge.exe", File.ReadAllBytes(exePath)); Add(z, "PortForge-9/README.md", Encoding.UTF8.GetBytes("readme")); });
        assets["slip"] = Zip(z => { Add(z, "../escape.txt", new byte[] { 1 }); Add(z, "PortForge.exe", File.ReadAllBytes(exePath)); });
        assets["noexe"] = Zip(z => Add(z, "README.md", new byte[] { 1 }));
        // Broken/partial .git and Source tree: older extractors failed on empty objects\info\ with a trailing backslash.
        assets["vcs"] = Zip(z =>
        {
            Add(z, "PortForge-9/PortForge.exe", File.ReadAllBytes(exePath));
            Add(z, "PortForge-9/README.md", Encoding.UTF8.GetBytes("readme"));
            Add(z, "PortForge-9/Engine/ENGINE.txt", Encoding.UTF8.GetBytes("version=9"));
            AddDir(z, "PortForge-9/Source/engine/src/.git/objects/info\\");
            Add(z, "PortForge-9/Source/engine/src/.git/HEAD", Encoding.UTF8.GetBytes("ref: refs/heads/main"));
            Add(z, "PortForge-9/Source/secret.txt", Encoding.UTF8.GetBytes("should-not-extract"));
            Add(z, "PortForge-9/.cache/junk.bin", new byte[] { 9 });
        });
        var thread = new Thread(Accept) { IsBackground = true }; thread.Start();
    }

    private static void Add(ZipArchive zip, string name, byte[] data) { using (var s = zip.CreateEntry(name).Open()) s.Write(data, 0, data.Length); }
    private static void AddDir(ZipArchive zip, string name) { zip.CreateEntry(name); }
    private static byte[] Zip(Action<ZipArchive> fill) { using (var m = new MemoryStream()) { using (var z = new ZipArchive(m, ZipArchiveMode.Create, true)) fill(z); return m.ToArray(); } }
    public static string Sha(byte[] data) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    private void Accept() { try { while (true) { var c = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(o => Handle(c)); } } catch (SocketException) { } catch (ObjectDisposedException) { } }

    private string Release(string tag, string asset, string digest)
    {
        string assetJson = asset == null ? "" : "{\"name\":\"PortForge-Windows-and-Source.zip\",\"browser_download_url\":\"" + BaseUrl + "/asset/" + asset + "\",\"digest\":\"" + digest + "\"},";
        return "{\"tag_name\":\"" + tag + "\",\"html_url\":\"https://github.com/o/r/releases/tag/" + tag + "\",\"body\":\"Fixes and speed\\nMore fixes\",\"assets\":[" + assetJson + "{\"name\":\"notes.txt\",\"browser_download_url\":\"" + BaseUrl + "/asset/notes\"}]}";
    }

    private void Handle(TcpClient client)
    {
        using (client)
        try
        {
            var stream = client.GetStream(); var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
            string request = reader.ReadLine(); if (request == null) return; string route = request.Split(' ')[1]; string line;
            while (!String.IsNullOrEmpty(line = reader.ReadLine())) { }
            byte[] body; string status = "200 OK";
            if (route.StartsWith("/asset/")) { string key = route.Substring(7); body = assets.ContainsKey(key) ? assets[key] : new byte[0]; }
            else switch (route)
            {
                case "/repos/o/new/releases/latest": body = Encoding.UTF8.GetBytes(Release("v9.0.0", "good", "sha256:" + Sha(assets["good"]))); break;
                case "/repos/o/same/releases/latest": body = Encoding.UTF8.GetBytes(Release("v0.7.0", "good", "")); break;
                case "/repos/o/old/releases/latest": body = Encoding.UTF8.GetBytes(Release("v0.1.0", "good", "")); break;
                case "/repos/o/badsum/releases/latest": body = Encoding.UTF8.GetBytes(Release("v9.0.0", "good", "sha256:" + new string('0', 64))); break;
                case "/repos/o/slip/releases/latest": body = Encoding.UTF8.GetBytes(Release("v9.0.0", "slip", "")); break;
                case "/repos/o/noexe/releases/latest": body = Encoding.UTF8.GetBytes(Release("v9.0.0", "noexe", "")); break;
                case "/repos/o/vcs/releases/latest": body = Encoding.UTF8.GetBytes(Release("v9.0.0", "vcs", "")); break;
                case "/repos/o/nopkg/releases/latest": body = Encoding.UTF8.GetBytes(Release("v9.0.0", null, "")); break;
                case "/repos/o/junk/releases/latest": body = Encoding.UTF8.GetBytes("<html>not json"); break;
                case "/repos/o/notag/releases/latest": body = Encoding.UTF8.GetBytes(Release("nightly", "good", "")); break;
                default: body = new byte[0]; status = "404 Not Found"; break;
            }
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length); stream.Write(body, 0, body.Length);
        }
        catch (IOException) { }
    }
    public void Dispose() { listener.Stop(); }
}

internal static class UpdateTests
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    private static string Reject(Action action, string name, string expect)
    {
        try { action(); }
        catch (Exception e) { Check(e.Message.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0, name + " (" + e.Message + ")"); return e.Message; }
        throw new Exception("FAIL: expected failure: " + name);
    }

    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "portforge-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var server = new ReleaseServer(args[0]))
            {
                var none = CancellationToken.None; var current = new Version(0, 7, 0, 0);
                Version v;
                Check(AppUpdates.TryParseVersion("v0.9.1", out v) && v == new Version(0, 9, 1, 0), "Release tags with a leading v are read as versions");
                Check(AppUpdates.TryParseVersion("1.2", out v) && v == new Version(1, 2, 0, 0) && AppUpdates.TryParseVersion("v1.2.3-beta.1", out v) && v == new Version(1, 2, 3, 0), "Short versions and pre-release suffixes are understood");
                Check(!AppUpdates.TryParseVersion("nightly", out v) && !AppUpdates.TryParseVersion("", out v), "Tags without a version number are not treated as versions");

                var found = AppUpdates.Check(server.BaseUrl, "o/new", current, none);
                Check(found.IsNewer && found.Latest == new Version(9, 0, 0, 0) && found.HasPackage && found.AssetName.Contains("Windows") && found.AssetUrl.EndsWith("/asset/good"), "A newer release is detected and the Windows package is chosen over other files");
                Check(found.Notes.Contains("Fixes and speed") && found.PageUrl.StartsWith("https://github.com/"), "Release notes and the release page link are kept");
                Check(found.AssetSha256.Length == 64, "GitHub's published checksum is read");
                Check(!AppUpdates.Check(server.BaseUrl, "o/same", current, none).IsNewer, "The same version is reported as up to date");
                Check(!AppUpdates.Check(server.BaseUrl, "o/old", current, none).IsNewer, "An older release never counts as an update");
                Check(!AppUpdates.Check(server.BaseUrl, "o/nopkg", current, none).HasPackage, "A release without a Windows download is reported without crashing");
                Reject(() => AppUpdates.Check(server.BaseUrl, "o/gone", current, none), "A repository with no release gives a plain explanation", "No published version");
                Reject(() => AppUpdates.Check(server.BaseUrl, "o/junk", current, none), "An unreadable GitHub answer is reported", "could not read");
                Reject(() => AppUpdates.Check(server.BaseUrl, "o/notag", current, none), "A release without a version number is reported", "no version number");
                Reject(() => AppUpdates.Check("http://127.0.0.1:1", "o/new", current, none), "An unreachable server explains the connection problem", "internet connection");
                Reject(() => AppUpdates.Check("http://example.com", "o/new", current, none), "Plain HTTP to a remote host is refused", "HTTPS");

                string work = Path.Combine(root, "work"); int lastPercent = -1;
                string package = AppUpdates.Download(found, work, p => lastPercent = p, none);
                Check(File.Exists(Path.Combine(package, "PortForge.exe")) && File.ReadAllText(Path.Combine(package, "README.md")) == "readme" && lastPercent == 100, "A verified update is downloaded, unpacked and reports progress");
                Check(!Directory.GetFiles(work, "*.zip").Any(), "The downloaded ZIP is removed after unpacking");

                string vcsWork = Path.Combine(root, "vcs");
                string vcsPkg = AppUpdates.Download(AppUpdates.Check(server.BaseUrl, "o/vcs", current, none), vcsWork, null, none);
                Check(File.Exists(Path.Combine(vcsPkg, "PortForge.exe")) && File.Exists(Path.Combine(vcsPkg, "Engine", "ENGINE.txt")), "A package with Source/.git still unpacks the program and Engine");
                Check(!Directory.Exists(Path.Combine(vcsPkg, "Source")), "Source is not extracted for in-app updates");
                Check(!Directory.Exists(Path.Combine(vcsPkg, ".cache")) &&
                      Directory.GetDirectories(vcsPkg, ".git", SearchOption.AllDirectories).Length == 0,
                    "VCS and cache folders from the ZIP are skipped");

                var badSum = AppUpdates.Check(server.BaseUrl, "o/badsum", current, none);
                Reject(() => AppUpdates.Download(badSum, Path.Combine(root, "w2"), null, none), "A package that does not match GitHub's checksum is discarded", "checksum");
                Check(!Directory.Exists(Path.Combine(root, "w2")) || Directory.GetFileSystemEntries(Path.Combine(root, "w2")).Length == 0, "A rejected package leaves no files behind");
                Reject(() => AppUpdates.Download(AppUpdates.Check(server.BaseUrl, "o/slip", current, none), Path.Combine(root, "w3"), null, none), "A package with a path outside its folder is rejected", "unsafe path");
                Check(!File.Exists(Path.Combine(root, "w3", "escape.txt")) && !File.Exists(Path.Combine(root, "escape.txt")), "Nothing is written outside the update folder");
                Reject(() => AppUpdates.Download(AppUpdates.Check(server.BaseUrl, "o/noexe", current, none), Path.Combine(root, "w4"), null, none), "A package without PortForge.exe is rejected", "does not contain");
                var newer = AppUpdates.Check(server.BaseUrl, "o/new", new Version(99, 0, 0, 0), none); newer.Latest = new Version(100, 0, 0, 0);
                Reject(() => AppUpdates.Download(newer, Path.Combine(root, "w5"), null, none), "A package that is not newer than the installed program is rejected", "not newer");
                Reject(() => AppUpdates.Download(new UpdateInfo { Current = current, Latest = new Version(9, 0), AssetUrl = "http://example.com/a.zip" }, Path.Combine(root, "w6"), null, none), "Downloads over plain HTTP from a remote host are refused", "HTTPS");
                Reject(() => AppUpdates.Download(AppUpdates.Check(server.BaseUrl, "o/nopkg", current, none), Path.Combine(root, "w7"), null, none), "Installing a release with no package explains what to do", "no Windows download");
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    try { AppUpdates.Download(found, Path.Combine(root, "w8"), null, cts.Token); Check(false, "Cancelling stops the download"); } catch (Exception) { Check(true, "Cancelling stops the download"); }
                }

                // Install: replaces program files, keeps backup and user data.
                string app = Path.Combine(root, "My Games ø", "PortForge"); Directory.CreateDirectory(Path.Combine(app, "NativeGames", "Mario"));
                File.WriteAllText(Path.Combine(app, "PortForge.exe"), "old program"); File.WriteAllText(Path.Combine(app, "last-game.txt"), "keep me");
                File.WriteAllText(Path.Combine(app, "NativeGames", "Mario", "save.dat"), "save");
                string stage = Path.Combine(root, "stage"); Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, "PortForge.exe"), "new program"); File.WriteAllText(Path.Combine(stage, "README.md"), "new readme");
                Directory.CreateDirectory(Path.Combine(stage, "Engine")); File.WriteAllText(Path.Combine(stage, "Engine", "ENGINE.txt"), "version=2.0");
                Directory.CreateDirectory(Path.Combine(app, "Engine")); File.WriteAllText(Path.Combine(app, "Engine", "old-library.dll"), "old");
                string log = AppUpdates.StartInstall(stage, app, 0, false);
                Wait(() => File.Exists(log) && File.ReadAllText(log).Contains("Update installed"), 40);
                Check(File.ReadAllText(Path.Combine(app, "PortForge.exe")) == "new program" && File.ReadAllText(Path.Combine(app, "README.md")) == "new readme", "The helper replaces the program files, even in a folder with spaces and non-English letters");
                Check(File.ReadAllText(Path.Combine(app, "update-backup", "PortForge.exe")) == "old program", "The previous program is kept as a backup");
                Check(File.ReadAllText(Path.Combine(app, "last-game.txt")) == "keep me" && File.ReadAllText(Path.Combine(app, "NativeGames", "Mario", "save.dat")) == "save", "Settings, converted games and saves are untouched");
                Check(File.ReadAllText(Path.Combine(app, "Engine", "ENGINE.txt")) == "version=2.0" && !File.Exists(Path.Combine(app, "Engine", "old-library.dll")), "An update replaces the whole PortForge Engine folder");
                Wait(() => !Directory.Exists(stage), 15);
                Check(!Directory.Exists(stage), "The unpacked update is cleaned up");

                // Waiting for the running app: the helper must not replace files before that process exits.
                var sleeper = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep 6") { CreateNoWindow = true, UseShellExecute = false });
                string app2 = Path.Combine(root, "app2"); Directory.CreateDirectory(app2); File.WriteAllText(Path.Combine(app2, "PortForge.exe"), "old2");
                string stage2 = Path.Combine(root, "stage2"); Directory.CreateDirectory(stage2); File.WriteAllText(Path.Combine(stage2, "PortForge.exe"), "new2");
                string log2 = AppUpdates.StartInstall(stage2, app2, sleeper.Id, false);
                Thread.Sleep(2500);
                Check(File.ReadAllText(Path.Combine(app2, "PortForge.exe")) == "old2" && !File.Exists(log2), "The helper waits for PortForge to close before replacing files");
                sleeper.WaitForExit(); Wait(() => File.Exists(log2) && File.ReadAllText(log2).Contains("Update installed"), 40);
                Check(File.ReadAllText(Path.Combine(app2, "PortForge.exe")) == "new2", "The update is installed once PortForge has closed");

                // Failure: a locked file leaves the old version in place and explains why.
                string app3 = Path.Combine(root, "app3"); Directory.CreateDirectory(app3); string locked = Path.Combine(app3, "PortForge.exe"); File.WriteAllText(locked, "old3");
                string stage3 = Path.Combine(root, "stage3"); Directory.CreateDirectory(stage3); File.WriteAllText(Path.Combine(stage3, "PortForge.exe"), "new3");
                string log3;
                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    log3 = AppUpdates.StartInstall(stage3, app3, 0, false);
                    Wait(() => File.Exists(log3) && File.ReadAllText(log3).Contains("failed"), 60);
                }
                Check(File.ReadAllText(locked) == "old3" && File.ReadAllText(log3).Contains("failed"), "A file that cannot be replaced leaves the old version working and is logged");
            }
            Console.WriteLine(checks + " update checks passed. Evidence: " + root); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Wait(Func<bool> condition, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) { try { if (condition()) return; } catch (IOException) { } Thread.Sleep(250); }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PortForge
{
    public sealed class UpdateInfo
    {
        public Version Current, Latest;
        public string Tag = "", AssetName = "", AssetUrl = "", AssetSha256 = "", Notes = "", PageUrl = "";
        public bool IsNewer { get { return Latest != null && Current != null && Latest > Current; } }
        public bool HasPackage { get { return AssetUrl.Length > 0; } }
    }

    public static class AppUpdates
    {
        public const string Repository = "toer15/PortForge";
        public const string ApiRoot = "https://api.github.com";
        private static readonly string[] PackageFiles = { "PortForge.exe", "PortForge.exe.config", "README.md", "DEVELOPING.md" };

        public static Version CurrentVersion
        {
            get { return typeof(AppUpdates).Assembly.GetName().Version; }
        }

        private static bool IsLoopback(Uri uri) { return uri.IsLoopback; }

        private static HttpWebRequest Request(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri))) throw new IOException("Updates are only downloaded over HTTPS.");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = "PortForge/" + CurrentVersion.ToString(3);
            request.Accept = "application/vnd.github+json";
            request.Timeout = 20000; request.ReadWriteTimeout = 30000;
            return request;
        }

        public static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (String.IsNullOrWhiteSpace(tag)) return false;
            string text = tag.Trim().TrimStart('v', 'V');
            int cut = text.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) text = text.Substring(0, cut);
            Version parsed;
            if (!Version.TryParse(text, out parsed)) return false;
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
            return true;
        }

        public static UpdateInfo Check(string apiRoot, string repository, Version current, CancellationToken token)
        {
            Uri uri = new Uri(apiRoot.TrimEnd('/') + "/repos/" + repository + "/releases/latest");
            string json;
            try
            {
                var request = Request(uri);
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) json = reader.ReadToEnd();
            }
            catch (WebException e)
            {
                var response = e.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound) throw new IOException("No published version was found yet. Publish a release on GitHub first.");
                if (response != null && (int)response.StatusCode == 403) throw new IOException("GitHub is limiting requests right now. Try again in a few minutes.");
                throw new IOException("Could not reach GitHub. Check your internet connection and try again.");
            }
            token.ThrowIfCancellationRequested();
            return Parse(json, current);
        }

        public static UpdateInfo Parse(string json, Version current)
        {
            var info = new UpdateInfo { Current = current };
            Dictionary<string, object> release;
            try { release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (ArgumentException) { throw new IOException("GitHub returned an answer the app could not read."); }
            if (release == null) throw new IOException("GitHub returned an answer the app could not read.");
            info.Tag = Text(release, "tag_name"); info.Notes = Text(release, "body"); info.PageUrl = Text(release, "html_url");
            Version latest;
            if (!TryParseVersion(info.Tag, out latest)) throw new IOException("The latest GitHub release has no version number (" + info.Tag + ").");
            info.Latest = latest;
            var assets = release.ContainsKey("assets") ? release["assets"] as ArrayList : null;
            if (assets != null)
            {
                var zips = assets.OfType<Dictionary<string, object>>().Where(a => Text(a, "name").EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Text(a, "browser_download_url").Length > 0).ToList();
                var chosen = zips.FirstOrDefault(a => Text(a, "name").IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0) ?? zips.FirstOrDefault();
                if (chosen != null)
                {
                    info.AssetName = Text(chosen, "name"); info.AssetUrl = Text(chosen, "browser_download_url");
                    string digest = Text(chosen, "digest");
                    if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.AssetSha256 = digest.Substring(7).Trim().ToLowerInvariant();
                }
            }
            return info;
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        /// <summary>Downloads and unpacks the update into a new folder inside workRoot and returns the folder holding the new PortForge.exe.</summary>
        public static string Download(UpdateInfo info, string workRoot, Action<int> progress, CancellationToken token)
        {
            if (!info.HasPackage) throw new IOException("This release has no Windows download attached. Open the release page to get it manually.");
            Directory.CreateDirectory(workRoot);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string zipPath = Path.Combine(workRoot, "update-" + stamp + ".zip"), folder = Path.Combine(workRoot, "update-" + stamp);
            try
            {
                var request = Request(new Uri(info.AssetUrl));
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var source = response.GetResponseStream())
                using (var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                {
                    long total = response.ContentLength, done = 0; var buffer = new byte[81920]; int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read); sha.TransformBlock(buffer, 0, read, null, 0); done += read;
                        if (progress != null && total > 0) progress((int)(done * 100 / total));
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    string actual = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    if (info.AssetSha256.Length > 0 && actual != info.AssetSha256) throw new IOException("The downloaded update does not match GitHub's checksum, so it was discarded.");
                }
                Extract(zipPath, folder);
                string exe = Directory.GetFiles(folder, "PortForge.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
                if (exe == null) throw new IOException("The update package does not contain PortForge.exe.");
                Version packaged;
                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out packaged) || packaged <= info.Current) throw new IOException("The update package is not newer than the version you have.");
                return Path.GetDirectoryName(exe);
            }
            catch
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { }
                throw;
            }
            finally { try { File.Delete(zipPath); } catch (IOException) { } }
        }

        /// <summary>Folders that must never be unpacked from an update ZIP (VCS metadata, build caches, optional Source tree).</summary>
        private static readonly string[] SkipExtractNames = { ".git", ".svn", ".hg", ".cache", ".claude", "Source" };

        private static bool ShouldSkipExtract(string entryName)
        {
            if (String.IsNullOrEmpty(entryName)) return true;
            string[] parts = entryName.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                foreach (string skip in SkipExtractNames)
                {
                    if (part.Equals(skip, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static bool IsDirectoryEntry(string entryName)
        {
            return entryName.EndsWith("/", StringComparison.Ordinal) || entryName.EndsWith("\\", StringComparison.Ordinal);
        }

        private static void Extract(string zipPath, string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            Directory.CreateDirectory(folder);
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var entry in zip.Entries)
                {
                    if (ShouldSkipExtract(entry.FullName)) continue;
                    string relative = entry.FullName.Replace('/', '\\').TrimEnd('\\');
                    if (relative.Length == 0) continue;
                    string destination = Path.GetFullPath(Path.Combine(folder, relative));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) || entry.FullName.IndexOf(':') >= 0) throw new IOException("The update package contains an unsafe path and was rejected.");
                    if (IsDirectoryEntry(entry.FullName)) { Directory.CreateDirectory(destination); continue; }
                    string parent = Path.GetDirectoryName(destination);
                    if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    entry.ExtractToFile(destination, true);
                }
        }

        /// <summary>Starts a helper that waits for this app to exit, swaps in the new program files (keeping a backup) and optionally reopens PortForge.</summary>
        public static string StartInstall(string packageFolder, string appFolder, int processId, bool relaunch)
        {
            string script = Path.Combine(Path.GetTempPath(), "PortForge-update-" + Guid.NewGuid().ToString("N") + ".ps1");
            string log = Path.Combine(appFolder, "update.log");
            var code = new StringBuilder();
            code.AppendLine("param([int]$WaitPid,[string]$Source,[string]$Target,[string]$Log,[int]$Relaunch)");
            code.AppendLine("$ErrorActionPreference = 'Stop'");
            code.AppendLine("function Note($m) { Add-Content -LiteralPath $Log -Value ((Get-Date -Format s) + ' ' + $m) -Encoding UTF8 }");
            code.AppendLine("$files = @(" + String.Join(",", PackageFiles.Select(f => "'" + f + "'")) + ")");
            code.AppendLine("try {");
            code.AppendLine("  if ($WaitPid -gt 0) { $p = Get-Process -Id $WaitPid -ErrorAction SilentlyContinue; if ($p) { [void]$p.WaitForExit(60000) } }");
            code.AppendLine("  $backup = Join-Path $Target 'update-backup'; New-Item -ItemType Directory -Force -Path $backup | Out-Null");
            code.AppendLine("  foreach ($f in $files) { $t = Join-Path $Target $f; if (Test-Path -LiteralPath $t) { Copy-Item -LiteralPath $t -Destination (Join-Path $backup $f) -Force } }");
            code.AppendLine("  try {");
            code.AppendLine("    foreach ($f in $files) { $s = Join-Path $Source $f; if (-not (Test-Path -LiteralPath $s)) { continue }");
            code.AppendLine("      for ($i = 0; $i -lt 20; $i++) { try { Copy-Item -LiteralPath $s -Destination (Join-Path $Target $f) -Force; break } catch { if ($i -eq 19) { throw }; Start-Sleep -Milliseconds 500 } } }");
            code.AppendLine("    $nt = Join-Path $Source 'NativeTest'; if (Test-Path -LiteralPath $nt) { Copy-Item -LiteralPath $nt -Destination $Target -Recurse -Force }");
            code.AppendLine("    $en = Join-Path $Source 'Engine'; if (Test-Path -LiteralPath $en) { $old = Join-Path $Target 'Engine'; if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force }; Copy-Item -LiteralPath $en -Destination $Target -Recurse -Force }");
            code.AppendLine("    Note 'Update installed.'");
            code.AppendLine("  } catch {");
            code.AppendLine("    foreach ($f in $files) { $b = Join-Path $backup $f; if (Test-Path -LiteralPath $b) { Copy-Item -LiteralPath $b -Destination (Join-Path $Target $f) -Force } }");
            code.AppendLine("    Note ('Update failed and the previous version was restored: ' + $_.Exception.Message)");
            code.AppendLine("  }");
            code.AppendLine("} catch { Note ('Update failed: ' + $_.Exception.Message) }");
            code.AppendLine("if ($Relaunch -eq 1) { Start-Process -FilePath (Join-Path $Target 'PortForge.exe') }");
            code.AppendLine("Remove-Item -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue");
            code.AppendLine("Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");
            File.WriteAllText(script, code.ToString(), new UTF8Encoding(true));
            var start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + GameManifest.Quote(script) + " -WaitPid " + processId + " -Source " + GameManifest.Quote(packageFolder.TrimEnd('\\')) +
                " -Target " + GameManifest.Quote(appFolder.TrimEnd('\\')) + " -Log " + GameManifest.Quote(log) + " -Relaunch " + (relaunch ? 1 : 0))
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(start);
            return log;
        }
    }
}

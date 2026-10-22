using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace PortForge
{
    public class BuildRequest
    {
        public string Title;
        public string Profile;
        public string RuntimeExe;
        public string GamePath;
        public string OutputFolder;
        public string[] Arguments;
        public bool Portable;
    }

    [XmlRoot("PortForge")]
    public class GameManifest
    {
        public int Version = 1;
        public string Title;
        public string Profile;
        public bool Portable;
        public string Runtime;
        public string Game;
        [XmlArrayItem("Argument")]
        public string[] Arguments;

        public static GameManifest Load(string file)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            using (var reader = XmlReader.Create(file, settings))
            {
                var manifest = (GameManifest)new XmlSerializer(typeof(GameManifest)).Deserialize(reader);
                if (manifest.Version != 1) throw new InvalidDataException("This package needs a newer version of PortForge.");
                if (String.IsNullOrWhiteSpace(manifest.Title) || manifest.Arguments == null)
                    throw new InvalidDataException("The package settings are incomplete.");
                return manifest;
            }
        }

        public void Save(string file)
        {
            using (var writer = XmlWriter.Create(file, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                new XmlSerializer(typeof(GameManifest)).Serialize(writer, this);
        }

        public ProcessStartInfo Resolve(string packageFolder)
        {
            string runtime = ResolvePath(packageFolder, Runtime);
            string game = ResolvePath(packageFolder, Game);
            if (!File.Exists(runtime)) throw new FileNotFoundException("The runtime executable is missing. Keep the whole exported folder together, or rebuild the package.", runtime);
            if (!File.Exists(game) && !Directory.Exists(game)) throw new FileNotFoundException("The game files are missing. Keep the whole exported folder together, or rebuild the package.", game);
            var args = Arguments.Select(a => Expand(a, game, runtime, Path.GetFullPath(packageFolder)));
            return new ProcessStartInfo(runtime, String.Join(" ", args.Select(Quote)))
            {
                WorkingDirectory = Path.GetDirectoryName(runtime),
                UseShellExecute = false
            };
        }

        private string ResolvePath(string root, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidDataException("A package path is empty.");
            if (!Portable)
            {
                if (!Path.IsPathRooted(value)) throw new InvalidDataException("A linked package needs absolute paths.");
                return Path.GetFullPath(value);
            }
            if (Path.IsPathRooted(value)) throw new InvalidDataException("A portable package must use relative paths.");
            string result = Path.GetFullPath(Path.Combine(root, value));
            if (!Paths.IsInside(result, root)) throw new InvalidDataException("A package path points outside its folder.");
            return result;
        }

        public static string Expand(string argument, string game, string runtime, string package)
        {
            if (argument == null || argument.IndexOf('\0') >= 0) throw new InvalidDataException("An argument is invalid.");
            return Regex.Replace(argument, @"\{([A-Za-z]+)\}", delegate(Match match)
            {
                switch (match.Groups[1].Value)
                {
                    case "game": return game;
                    case "gameDir": return Directory.Exists(game) ? game : Path.GetDirectoryName(game);
                    case "runtime": return Path.GetDirectoryName(runtime);
                    case "package": return package;
                    default: throw new InvalidDataException("Unknown argument placeholder: " + match.Value);
                }
            });
        }

        // Windows command-line escaping, including embedded quotes and trailing backslashes.
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }

    public static class Paths
    {
        public static bool IsInside(string child, string parent)
        {
            string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Overlap(string a, string b)
        {
            return String.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || IsInside(a, b) || IsInside(b, a);
        }

        public static void CheckLinks(string path)
        {
            for (string item = Path.GetFullPath(path); !String.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
            {
                if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Choose a real local file or folder instead of a link or junction: " + item);
            }
        }
    }

    public class BuildPlan
    {
        public string Destination;
        public long Bytes;
        public int Files;
    }

    public static class PackageBuilder
    {
        private class CopyItem
        {
            public string Source;
            public string Target;
            public long Bytes;
            public bool Directory;
        }

        public static void Validate(BuildRequest request)
        {
            string title = request.Title;
            if (String.IsNullOrWhiteSpace(title) || title != title.Trim() || title.Length > 64 || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || title.EndsWith("."))
                throw new ArgumentException("Enter a game name of 1-64 characters without Windows filename symbols or a trailing dot.");
            if (Regex.IsMatch(title.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                throw new ArgumentException("That name is reserved by Windows. Choose another game name.");
            if (!File.Exists(request.RuntimeExe) || !String.Equals(Path.GetExtension(request.RuntimeExe), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose your emulator or runtime's Windows .exe file.");
            if (!File.Exists(request.GamePath) && !Directory.Exists(request.GamePath))
                throw new ArgumentException("Choose an existing game file or game folder.");
            if (String.IsNullOrWhiteSpace(request.OutputFolder) || !Path.IsPathRooted(request.OutputFolder))
                throw new ArgumentException("Choose an absolute export folder.");
            if (request.Arguments == null || request.Arguments.Length == 0 || !request.Arguments.Any(a => a != null && a.Contains("{game}")))
                throw new ArgumentException("Add {game} as a launch argument. Each line is one argument; quotes around paths are added for you.");
            foreach (string arg in request.Arguments) GameManifest.Expand(arg, request.GamePath, request.RuntimeExe, request.OutputFolder);
            string destination = Path.GetFullPath(Path.Combine(request.OutputFolder, title));
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException("An export with that name already exists. Choose another name or export folder.");
            Paths.CheckLinks(request.RuntimeExe);
            Paths.CheckLinks(request.GamePath);
            Paths.CheckLinks(request.OutputFolder);
            if (request.Portable)
            {
                string runtimeFolder = Path.GetDirectoryName(Path.GetFullPath(request.RuntimeExe));
                string gameFolder = Directory.Exists(request.GamePath) ? request.GamePath : Path.GetDirectoryName(Path.GetFullPath(request.GamePath));
                if (Paths.Overlap(runtimeFolder, destination) || Paths.Overlap(gameFolder, destination))
                    throw new IOException("Choose an export folder outside the runtime and game folders, so files cannot copy into themselves.");
                if (String.Equals(runtimeFolder.TrimEnd('\\'), Path.GetPathRoot(runtimeFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                    Paths.Overlap(runtimeFolder, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
                    throw new IOException("Use an emulator in its own folder. A drive root or Windows folder cannot be packaged as a runtime.");
            }
        }

        private static List<CopyItem> Scan(BuildRequest request, CancellationToken cancellation)
        {
            var files = new List<CopyItem>();
            if (!request.Portable) return files;
            ScanDirectory(Path.GetDirectoryName(Path.GetFullPath(request.RuntimeExe)), "runtime", files, cancellation);
            if (Directory.Exists(request.GamePath)) ScanDirectory(Path.GetFullPath(request.GamePath), "game", files, cancellation);
            else files.Add(new CopyItem { Source = Path.GetFullPath(request.GamePath), Target = Path.Combine("game", Path.GetFileName(request.GamePath)), Bytes = new FileInfo(request.GamePath).Length });
            return files;
        }

        private static void ScanDirectory(string source, string target, List<CopyItem> files, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Paths.CheckLinks(source);
            files.Add(new CopyItem { Source = source, Target = target, Directory = true });
            foreach (string file in Directory.EnumerateFiles(source))
            {
                cancellation.ThrowIfCancellationRequested();
                Paths.CheckLinks(file);
                files.Add(new CopyItem { Source = file, Target = Path.Combine(target, Path.GetFileName(file)), Bytes = new FileInfo(file).Length });
            }
            foreach (string folder in Directory.EnumerateDirectories(source))
                ScanDirectory(folder, Path.Combine(target, Path.GetFileName(folder)), files, cancellation);
        }

        public static BuildPlan Inspect(BuildRequest request, CancellationToken cancellation)
        {
            Validate(request);
            var files = Scan(request, cancellation);
            return new BuildPlan { Destination = Path.Combine(Path.GetFullPath(request.OutputFolder), request.Title), Bytes = files.Sum(f => f.Bytes), Files = files.Count(f => !f.Directory) };
        }

        public static string Build(BuildRequest request, byte[] launcher, IProgress<string> progress, CancellationToken cancellation)
        {
            Validate(request);
            if (launcher == null || launcher.Length < 2 || launcher[0] != 'M' || launcher[1] != 'Z') throw new InvalidDataException("The launcher resource is missing or damaged.");
            progress.Report("Checking files and available space...");
            var files = Scan(request, cancellation);
            long bytes = files.Sum(f => f.Bytes);
            int fileCount = files.Count(f => !f.Directory);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(request.OutputFolder)));
            if (drive.IsReady && drive.AvailableFreeSpace < bytes + launcher.Length + 8 * 1024 * 1024)
                throw new IOException("There is not enough free space for this package (" + FormatBytes(bytes) + ").");
            Directory.CreateDirectory(request.OutputFolder);
            string destination = Path.Combine(Path.GetFullPath(request.OutputFolder), request.Title);
            string staging = Path.Combine(Path.GetFullPath(request.OutputFolder), ".portforge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                int copied = 0;
                byte[] buffer = new byte[1024 * 1024];
                foreach (var file in files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Paths.CheckLinks(file.Source);
                    string target = Path.Combine(staging, file.Target);
                    if (file.Directory) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    progress.Report("Copying " + (++copied) + " / " + fileCount + "  |  " + file.Target);
                    using (var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, count);
                        }
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(staging, request.Title + ".exe"), launcher);
                var manifest = new GameManifest
                {
                    Title = request.Title,
                    Profile = request.Profile,
                    Portable = request.Portable,
                    Runtime = request.Portable ? Path.Combine("runtime", Path.GetFileName(request.RuntimeExe)) : Path.GetFullPath(request.RuntimeExe),
                    Game = request.Portable ? (Directory.Exists(request.GamePath) ? "game" : Path.Combine("game", Path.GetFileName(request.GamePath))) : Path.GetFullPath(request.GamePath),
                    Arguments = request.Arguments
                };
                // Preserve empty game/runtime folders as well as files.
                if (request.Portable)
                {
                    Directory.CreateDirectory(Path.Combine(staging, "runtime"));
                    Directory.CreateDirectory(Path.Combine(staging, "game"));
                }
                manifest.Save(Path.Combine(staging, "portforge.xml"));
                File.WriteAllText(Path.Combine(staging, "START-HERE.txt"), request.Title + "\r\n\r\nDouble-click " + request.Title + ".exe to play.\r\nKeep this entire folder together.\r\n\r\n" +
                    (request.Portable ? "Runtime and game files were copied into this folder. External firmware, keys, user profiles, installed dependencies, and saves are not automatically included. Configure your runtime's portable mode if needed.\r\n" : "This is a linked package. It uses files at their original locations on the build PC.\r\n") +
                    "\r\nThis package runs the selected emulator/runtime; it is not a native game recompilation. Compatibility and speed depend on that runtime.\r\n" +
                    "Launch logs: %LOCALAPPDATA%\\PortForge\\Logs\r\n\r\nUse game and runtime files you have permission to use. Check their licenses before sharing a package.\r\n");
                cancellation.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
                progress.Report("Export complete: " + destination);
                return destination;
            }
            finally
            {
                // The staging path is generated here, inside the chosen export folder.
                if (Directory.Exists(staging) && Paths.IsInside(staging, request.OutputFolder))
                    Directory.Delete(staging, true);
            }
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }
}

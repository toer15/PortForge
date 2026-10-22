using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace PortForge
{
    public sealed class DownloadRequest
    {
        public string Url, Destination, Sha256;
        public int Connections = 8;
    }
    public sealed class DownloadProgress
    {
        public string Message;
        public long Received, Total;
        public double BytesPerSecond;
    }
    public sealed class DownloadResult
    {
        public string File, Sha256, Kind;
        public bool Reused;
    }
    public static class GameDownloads
    {
        private sealed class Metadata { public long Length = -1; public string ETag = "", Source; public bool Ranges; public int Parts = 1; }
        private sealed class RangeFailure : IOException { public RangeFailure() : base("The server changed its range response.") { } }
        private sealed class SourceFailure : IOException { public int Code; public SourceFailure(string s, int code = 0) : base(s) { Code = code; } }
        public static Uri Validate(DownloadRequest r)
        {
            Uri uri;
            if (r == null || !Uri.TryCreate(r.Url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo.Length > 0)
                throw new ArgumentException("Paste a direct HTTP or HTTPS file link. Login pages and browser-only links need a fresh direct link.");
            if (String.IsNullOrWhiteSpace(r.Destination) || !Path.IsPathRooted(r.Destination)) throw new ArgumentException("Choose an absolute Save as path.");
            string ext = Path.GetExtension(r.Destination).ToLowerInvariant();
            if (!new[] {".nro", ".nsp", ".xci", ".dnsp", ".dxci", ".zip"}.Contains(ext)) throw new ArgumentException("Save as an .nro, .nsp, .xci or .zip file. Unpack RAR/7z archives first.");
            if (r.Connections < 1 || r.Connections > 16) throw new ArgumentException("Choose between one and sixteen download connections.");
            if (!String.IsNullOrWhiteSpace(r.Sha256) && !Regex.IsMatch(r.Sha256.Trim(), "^[0-9a-fA-F]{64}$")) throw new ArgumentException("The optional SHA-256 must contain exactly 64 hexadecimal characters.");
            Paths.CheckLinks(r.Destination);
            return uri;
        }
        private static string HashBytes(byte[] bytes) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string HashFile(string path, CancellationToken token)
        {
            using (var h = SHA256.Create()) using (var file = File.OpenRead(path))
            {
                byte[] buffer = new byte[1024 * 1024]; int n;
                while ((n = file.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); h.TransformBlock(buffer, 0, n, buffer, 0); }
                h.TransformFinalBlock(new byte[0], 0, 0); return BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void Tell(IProgress<DownloadProgress> progress, string message, long received = 0, long total = -1, double speed = 0)
        { if (progress != null) progress.Report(new DownloadProgress { Message = message, Received = received, Total = total, BytesPerSecond = speed }); }
        private static HttpWebRequest Request(Uri uri, long? start, long? end, string etag)
        {
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.UserAgent = "PortForge/0.6"; req.Timeout = 30000; req.ReadWriteTimeout = 30000;
            req.AllowAutoRedirect = true; req.MaximumAutomaticRedirections = 5; req.AutomaticDecompression = DecompressionMethods.None;
            req.Headers[HttpRequestHeader.AcceptEncoding] = "identity"; req.ServicePoint.ConnectionLimit = Math.Max(req.ServicePoint.ConnectionLimit, 16); req.ServicePoint.UseNagleAlgorithm = false; req.ServicePoint.Expect100Continue = false;
            if (start.HasValue) { if (end.HasValue) req.AddRange(start.Value, end.Value); else req.AddRange(start.Value); }
            if (!String.IsNullOrEmpty(etag) && start.HasValue) req.Headers["If-Range"] = etag;
            return req;
        }
        private static bool Transient(WebException e)
        {
            var response = e.Response as HttpWebResponse;
            if (response != null) { int code = (int)response.StatusCode; return code == 408 || code == 429 || code >= 500; }
            return e.Status != WebExceptionStatus.TrustFailure && e.Status != WebExceptionStatus.SecureChannelFailure;
        }
        private static void RetryWait(int attempt, CancellationToken token, IProgress<DownloadProgress> progress, WebException error)
        {
            int seconds = Math.Min(16, 1 << attempt); var response = error == null ? null : error.Response as HttpWebResponse;
            if (response != null)
            {
                int after; DateTime date;
                string value = response.Headers["Retry-After"];
                if (Int32.TryParse(value, out after)) seconds = Math.Max(seconds, Math.Min(120, after));
                else if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out date)) seconds = Math.Max(seconds, Math.Min(120, (int)(date - DateTime.UtcNow).TotalSeconds));
            }
            Tell(progress, "Connection interrupted or server busy. Retrying in " + seconds + " seconds (" + (attempt + 1) + "/4).");
            if (token.WaitHandle.WaitOne(seconds * 1000)) token.ThrowIfCancellationRequested();
        }
        private static HttpWebResponse Open(HttpWebRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try { return (HttpWebResponse)request.GetResponse(); }
            catch (WebException e)
            {
                token.ThrowIfCancellationRequested();
                var response = e.Response as HttpWebResponse;
                if (response != null && !Transient(e))
                {
                    int code = (int)response.StatusCode; response.Close();
                    throw new SourceFailure("The source returned HTTP " + code + ". Check the direct link. Expired links or sign-in requirements need a new link from the source.",code);
                }
                throw;
            }
        }
        private static Metadata Probe(Uri uri, CancellationToken token, IProgress<DownloadProgress> progress)
        {
            bool rangeProbe = true;
            for (int attempt = 0; ; attempt++)
            {
                var req = Request(uri, rangeProbe ? (long?)0 : null, rangeProbe ? (long?)0 : null, null);
                using (token.Register(req.Abort))
                try
                {
                    using (var response = Open(req, token))
                    {
                        if (response.ContentType != null && (response.ContentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0 || response.ContentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0))
                            throw new SourceFailure("That link returned a web page or API response. Paste the direct game-file download link.");
                        var meta = new Metadata { Source = HashBytes(Encoding.UTF8.GetBytes(uri.AbsoluteUri)), Length = response.ContentLength };
                        string etag = response.Headers[HttpResponseHeader.ETag];
                        if (!String.IsNullOrEmpty(etag) && etag.StartsWith("\"", StringComparison.Ordinal) && etag.EndsWith("\"", StringComparison.Ordinal)) meta.ETag = etag;
                        var range = Regex.Match(response.Headers["Content-Range"] ?? "", @"^bytes 0-0/(\d+)$");
                        if (response.StatusCode == HttpStatusCode.PartialContent && !range.Success) throw new RangeFailure();
                        if (response.StatusCode == HttpStatusCode.PartialContent && range.Success) { meta.Length = Int64.Parse(range.Groups[1].Value); meta.Ranges = true; }
                        if (meta.Length == 0) throw new SourceFailure("The source returned an empty file.");
                        return meta;
                    }
                }
                catch (SourceFailure e) { if (rangeProbe && (e.Code == 400 || e.Code == 405 || e.Code == 416)) { rangeProbe = false; continue; } throw; }
                catch (RangeFailure) { if (rangeProbe) { rangeProbe = false; continue; } throw; }
                catch (WebException e) { if (!Transient(e) || attempt >= 4) { if (e.Response != null) e.Response.Close(); throw; } RetryWait(attempt, token, progress, e); if (e.Response != null) e.Response.Close(); }
            }
        }
        private static void WriteMetadata(string path, Metadata m, string sha)
        {
            var text = new StringBuilder();
            using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                writer.WriteStartElement("PortForgeDownload"); writer.WriteAttributeString("version", "1");
                writer.WriteAttributeString("source", m.Source); writer.WriteAttributeString("length", m.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("etag", m.ETag); writer.WriteAttributeString("parts", m.Parts.ToString()); writer.WriteAttributeString("sha256", sha ?? ""); writer.WriteEndElement();
            }
            GameOptimization.AtomicWrite(path, Encoding.UTF8.GetBytes(text.ToString()), null);
        }
        private static Metadata ReadMetadata(string path, out string sha)
        {
            var doc = new XmlDocument { XmlResolver = null };
            Paths.CheckLinks(path);
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100000 })) doc.Load(reader);
            var e = doc.DocumentElement;
            if (e == null || e.Name != "PortForgeDownload" || e.GetAttribute("version") != "1") throw new IOException("The download recovery file is not recognized. Choose a different Save as path.");
            var m = new Metadata { Source = e.GetAttribute("source"), ETag = e.GetAttribute("etag"), Length = Int64.Parse(e.GetAttribute("length")), Parts = Int32.Parse(e.GetAttribute("parts")) };
            if (m.Parts < 1 || m.Parts > 16 || m.Length < -1) throw new IOException("The download recovery file is damaged. Choose a different Save as path.");
            sha = e.GetAttribute("sha256"); return m;
        }
        private static string Part(string work, int i) { return Path.Combine(work, "part-" + i + ".bin"); }
        private static void ClearParts(string work)
        {
            // Only fixed files belonging to this downloader are removed. Never recursively remove a folder.
            foreach (string file in Enumerable.Range(0, 16).Select(i => Part(work, i)).Concat(new[] { Path.Combine(work, "assembled.tmp") }))
            { if (!Paths.IsInside(file, work)) throw new IOException("Invalid download work path."); Paths.CheckLinks(file); if (File.Exists(file)) File.Delete(file); }
        }
        private static void Space(string path, long needed)
        {
            if (needed < 0) return;
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)));
            if (drive.IsReady && drive.AvailableFreeSpace < needed + 16L * 1024 * 1024) throw new IOException("Not enough free space for the download and its temporary copy. Free space or choose a different drive; completed parts are kept.");
        }
        private static void Transfer(Uri uri, Metadata m, string work, int index, CancellationToken token, IProgress<DownloadProgress> progress, Action update)
        {
            long begin = m.Parts == 1 ? 0 : m.Length * index / m.Parts;
            long end = m.Length < 0 ? -1 : m.Parts == 1 ? m.Length - 1 : m.Length * (index + 1) / m.Parts - 1;
            string file = Part(work, index); Paths.CheckLinks(file);
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                long local = File.Exists(file) ? new FileInfo(file).Length : 0;
                long size = end < 0 ? -1 : end - begin + 1;
                if (size >= 0 && local == size) return;
                if (local > size && size >= 0) { File.Delete(file); local = 0; }
                bool ranged = m.Ranges && m.ETag.Length > 0 && (m.Parts > 1 || local > 0);
                if (!ranged && local > 0) { File.Delete(file); local = 0; }
                var req = Request(uri, ranged ? (long?)(begin + local) : null, ranged && end >= 0 ? (long?)end : null, ranged ? m.ETag : null);
                using (token.Register(req.Abort))
                try
                {
                    using (var response = Open(req, token))
                    {
                        if (ranged)
                        {
                            string expected = "bytes " + (begin + local) + "-" + end + "/" + m.Length;
                            if (response.StatusCode != HttpStatusCode.PartialContent || response.Headers["Content-Range"] != expected || response.Headers[HttpResponseHeader.ETag] != m.ETag) throw new RangeFailure();
                        }
                        else if (response.StatusCode != HttpStatusCode.OK) throw new SourceFailure("The source did not return a complete file.");
                        using (var source = response.GetResponseStream()) using (var dest = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
                        {
                            byte[] buffer = new byte[256 * 1024]; int n;
                            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                if (size >= 0 && local + n > size) throw new RangeFailure();
                                dest.Write(buffer, 0, n); local += n; update();
                            }
                        }
                        if (size >= 0 && local != size) throw new EndOfStreamException("The connection ended before the complete file arrived.");
                        return;
                    }
                }
                catch (RangeFailure) { throw; }
                catch (SourceFailure) { throw; }
                catch (WebException e) { token.ThrowIfCancellationRequested(); if (!Transient(e) || attempt >= 4) { if (e.Response != null) e.Response.Close(); throw; } RetryWait(attempt, token, progress, e); if (e.Response != null) e.Response.Close(); }
                catch (IOException) { token.ThrowIfCancellationRequested(); if (attempt >= 4) throw; RetryWait(attempt, token, progress, null); }
            }
        }
        public static string FileKind(string path)
        {
            string kind = NativeConversion.DetectInput(path); if (kind != null) return kind;
            using (var file = File.OpenRead(path)) { byte[] h = new byte[4]; if (file.Read(h, 0, 4) == 4 && h[0] == 80 && h[1] == 75 && ((h[2] == 3 && h[3] == 4) || (h[2] == 5 && h[3] == 6) || (h[2] == 7 && h[3] == 8))) return "ZIP"; }
            throw new InvalidDataException("The downloaded file is not a recognized Switch container, NRO or ZIP. A web page, unsupported compressed file, or damaged source cannot be converted. Check the source link.");
        }
        public static DownloadResult Download(DownloadRequest r, IProgress<DownloadProgress> progress, CancellationToken token)
        {
            Uri uri = Validate(r); string destination = Path.GetFullPath(r.Destination), parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent); token.ThrowIfCancellationRequested(); Tell(progress, "Checking the source server and existing download.");
            string expectedHash = String.IsNullOrWhiteSpace(r.Sha256) ? "" : r.Sha256.Trim().ToLowerInvariant();
            string receipt = destination + ".portforge-receipt.xml", work = destination + ".portforge-download";
            Paths.CheckLinks(receipt);
            if (File.Exists(destination) && expectedHash.Length > 0 && HashFile(destination,token) == expectedHash)
            { Tell(progress,"The source checksum matches your existing file. Reusing it without a network transfer."); return new DownloadResult { File=destination, Sha256=expectedHash, Kind=FileKind(destination), Reused=true }; }
            Metadata meta = Probe(uri, token, progress); meta.Parts = meta.Ranges && meta.ETag.Length > 0 && meta.Length >= 1024 * 1024 ? r.Connections : 1;
            if (File.Exists(destination))
            {
                string acceptedHash = expectedHash;
                if (acceptedHash.Length == 0 && File.Exists(receipt))
                {
                    string oldHash; var old = ReadMetadata(receipt, out oldHash);
                    if (old.Source == meta.Source && old.Length == meta.Length && meta.ETag.Length > 0 && old.ETag == meta.ETag) acceptedHash = oldHash;
                }
                if (acceptedHash.Length > 0 && HashFile(destination, token) == acceptedHash)
                { Tell(progress, "This verified file is already downloaded. Reusing it."); return new DownloadResult { File = destination, Sha256 = acceptedHash, Kind = FileKind(destination), Reused = true }; }
                throw new IOException("A different or unverified file already exists at Save as. Choose another filename; your existing file was left untouched.");
            }
            Paths.CheckLinks(work); Directory.CreateDirectory(work);
            string manifest = Path.Combine(work, "download.xml");
            Paths.CheckLinks(manifest); Paths.CheckLinks(Path.Combine(work,"active.lock"));
            using (var locked = new FileStream(Path.Combine(work, "active.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                string unused;
                if (File.Exists(manifest))
                {
                    var old = ReadMetadata(manifest, out unused);
                    if (old.Source == meta.Source && old.Length == meta.Length && old.ETag.Length > 0 && old.ETag == meta.ETag && meta.Ranges) meta.Parts = old.Parts;
                    else { Tell(progress, "The source changed or cannot safely resume. Restarting its temporary download."); ClearParts(work); }
                }
                else if (Directory.EnumerateFiles(work).Any(f => Path.GetFileName(f) != "active.lock")) throw new IOException("The temporary download folder contains unrecognized files. Choose another Save as path.");
                WriteMetadata(manifest, meta, null);
                long existing = Enumerable.Range(0, meta.Parts).Sum(i => File.Exists(Part(work, i)) ? new FileInfo(Part(work, i)).Length : 0);
                Space(destination, meta.Length < 0 ? -1 : checked(meta.Length * 2 - Math.Min(existing, meta.Length)));
                var watch = Stopwatch.StartNew(); long lastTick = 0, previousBytes = existing; double previousTime = 0;
                var progressLock = new object();
                Action update = delegate
                {
                    lock (progressLock)
                    {
                        if (watch.ElapsedMilliseconds - lastTick < 300) return;
                        lastTick = watch.ElapsedMilliseconds;
                        long bytes = Enumerable.Range(0, meta.Parts).Sum(i => File.Exists(Part(work, i)) ? new FileInfo(Part(work, i)).Length : 0);
                        double now = watch.Elapsed.TotalSeconds, speed = Math.Max(0, (bytes - previousBytes) / Math.Max(.01, now - previousTime));
                        previousBytes = bytes; previousTime = now;
                        Tell(progress, "Downloading with " + meta.Parts + " connection(s).", bytes, meta.Length, speed);
                    }
                };
                for (int integrityAttempt = 0; ; integrityAttempt++)
                {
                    Tell(progress, "Downloading with " + meta.Parts + " connection(s). " + (meta.Ranges && meta.ETag.Length > 0 ? "Completed parts can resume after an interruption." : "This server cannot safely resume; interrupted attempts restart."), existing, meta.Length);
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        Exception failure = null;
                        var tasks = Enumerable.Range(0, meta.Parts).Select(i => Task.Run(() => { try { Transfer(uri, meta, work, i, linked.Token, progress, update); } catch (Exception e) { Interlocked.CompareExchange(ref failure, e, null); linked.Cancel(); } })).ToArray();
                        Task.WaitAll(tasks); token.ThrowIfCancellationRequested();
                        if (failure != null)
                        {
                            if ((failure is RangeFailure && meta.Ranges) || (failure is SourceFailure && meta.Parts > 1))
                            {
                                Tell(progress, "The source refused ranges or changed its file. Falling back to one complete connection.");
                                meta = Probe(uri,token,progress); meta.Parts = 1; meta.Ranges = false; ClearParts(work);
                                Space(destination,meta.Length < 0 ? -1 : checked(meta.Length * 2)); WriteMetadata(manifest, meta, null); integrityAttempt--; continue;
                            }
                            throw new IOException("Download paused after a connection or source error. Completed parts were kept. " + failure.Message, failure);
                        }
                    }
                    string assembled = Path.Combine(work, "assembled.tmp"); Paths.CheckLinks(assembled);
                    Tell(progress, "Checking the completed file. Large games can take a while.", meta.Length, meta.Length);
                    string actual;
                    // One pass: the parts are joined and hashed as they are copied, instead of copying and then reading the whole file again.
                    using (var hasher = SHA256.Create())
                    {
                        using (var output = new FileStream(assembled, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
                        foreach (int i in Enumerable.Range(0, meta.Parts)) using (var part = new FileStream(Part(work, i), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
                        { byte[] buffer = new byte[4 * 1024 * 1024]; int n; while ((n = part.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, n); hasher.TransformBlock(buffer, 0, n, null, 0); } }
                        hasher.TransformFinalBlock(new byte[0], 0, 0); actual = BitConverter.ToString(hasher.Hash).Replace("-", "").ToLowerInvariant();
                    }
                    if (expectedHash.Length > 0 && expectedHash != actual)
                    {
                        if (integrityAttempt < 1) { Tell(progress, "Checksum mismatch. Automatically downloading a fresh copy once."); ClearParts(work); continue; }
                        throw new InvalidDataException("SHA-256 still does not match after a fresh download. The source file or supplied checksum needs checking; no completed game file was published.");
                    }
                    string kind;
                    try
                    {
                        kind = FileKind(assembled);
                        if (kind == "ZIP") using (var zip = ZipFile.OpenRead(assembled)) { if (zip.Entries.Count == 0) throw new InvalidDataException("The ZIP is empty."); }
                    }
                    catch (InvalidDataException e)
                    {
                        if (integrityAttempt < 1) { Tell(progress,"The game header or ZIP structure is damaged. Automatically trying a fresh download once."); ClearParts(work); continue; }
                        throw new InvalidDataException("The source still returns an unrecognized game or damaged ZIP after a fresh download. Check the source file or direct link. " + e.Message,e);
                    }
                    if ((Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase)) != (kind == "ZIP")) throw new InvalidDataException("The file type does not match the Save as extension. Use .zip for archives, or the correct game extension for an unpacked game.");
                    token.ThrowIfCancellationRequested(); Paths.CheckLinks(destination);
                    WriteMetadata(receipt, meta, actual); File.Move(assembled, destination);
                    try { ClearParts(work); File.Delete(manifest); }
                    catch (IOException) { Tell(progress,"Download saved. Some temporary parts could not be removed; the completed file is ready."); }
                    catch (UnauthorizedAccessException) { Tell(progress,"Download saved. Some temporary parts could not be removed; the completed file is ready."); }
                    Tell(progress, expectedHash.Length > 0 ? "Download complete. The source checksum matches." : "Download complete. File type checked; no source checksum was supplied.", new FileInfo(destination).Length, new FileInfo(destination).Length);
                    return new DownloadResult { File = destination, Sha256 = actual, Kind = kind };
                }
            }
        }
        public static IList<string> Unpack(string zip, IProgress<DownloadProgress> progress, CancellationToken token)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(zip)), root = Path.Combine(parent, Path.GetFileNameWithoutExtension(zip) + "-files-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var archive = ZipFile.OpenRead(zip))
            {
                if (archive.Entries.Count > 200000) throw new InvalidDataException("The ZIP has too many files to unpack automatically.");
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
                foreach (var entry in archive.Entries)
                {
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0) throw new InvalidDataException("The ZIP contains an unsafe path and cannot be unpacked automatically.");
                    string path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!Paths.IsInside(path, root) || !targets.Add(path) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0)
                        throw new InvalidDataException("The ZIP contains an unsafe, duplicate or linked path and cannot be unpacked automatically.");
                    total = checked(total + entry.Length);
                }
                Space(parent, total); Paths.CheckLinks(root); Directory.CreateDirectory(root);
                Tell(progress, "Unpacking the ZIP. The original download is kept.");
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested(); string path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { byte[] buffer = new byte[1024 * 1024]; int n; while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, n); } }
                }
            }
            var games = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => new[] {".nro", ".nsp", ".xci", ".dnsp", ".dxci"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Where(p => NativeConversion.DetectInput(p) != null).ToList();
            if (games.Count == 0) throw new InvalidDataException("The ZIP was unpacked to " + root + ", but contains no supported game file. Choose an unpacked .nro/.nsp/.xci or an extracted game folder from the main screen.");
            return games;
        }
    }
}

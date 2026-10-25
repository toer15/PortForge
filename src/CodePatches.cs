using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PortForge
{
    /// <summary>One code module change from a .pchtxt: bytes written at an offset of the module's memory image.</summary>
    public sealed class CodeEdit
    {
        public long Offset;
        public byte[] Bytes;
    }

    /// <summary>The enabled edits of one .pchtxt for one module, chosen by the module's build ID.</summary>
    public sealed class CodePatch
    {
        public string Source, BuildId;
        public List<CodeEdit> Edits = new List<CodeEdit>();
    }

    /// <summary>
    /// Code patches in the .pchtxt text format Switch emulators read (a game's 60 FPS patch is one): applied to the game's
    /// program before it is compiled, so the compiled code and the runtime's JIT fallback both run the patched
    /// instructions. Each patched module is written, uncompressed, to the game's mods folder
    /// (&lt;game&gt;_data/mods/PortForge code patches/exefs/), which the engine compiles and runs the game with.
    /// A patch only applies to the module whose build ID it names, so a folder of patches for many games is safe.
    /// </summary>
    public static class CodePatches
    {
        public const string ModName = "PortForge code patches";
        /// <summary>Patches PortForge applies when it converts a game: put .pchtxt files here.</summary>
        public static string FolderPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortForge", "patches"); } }
        static readonly string[] Modules = { "main", "rtld", "sdk", "subsdk0", "subsdk1", "subsdk2", "subsdk3", "subsdk4", "subsdk5", "subsdk6", "subsdk7", "subsdk8", "subsdk9" };

        /// <summary>
        /// The enabled patch sets of a .pchtxt. "@nsobid-ID" starts a module's patches, "@flag offset_shift N" is added
        /// to the offsets that follow, "@enabled" and "@disabled" turn the lines after them on and off, "@stop" ends the
        /// file. A line is "OFFSET HEXBYTES" or "OFFSET "text"", both offsets in hex. Like the emulators' IPS for NSOs,
        /// an offset plus its shift counts from the 0x100-byte NSO header, so the memory offset is that minus 0x100.
        /// </summary>
        public static List<CodePatch> Parse(string text, string source)
        {
            var patches = new List<CodePatch>();
            CodePatch current = null;
            long shift = 0;
            bool enabled = false;
            int number = 0;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                number++;
                string line = raw;
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0 && line.IndexOf('"') < 0) line = line.Substring(0, comment);
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("@nsobid-", StringComparison.OrdinalIgnoreCase))
                {
                    string id = line.Substring(8).Trim();
                    if (id.Length == 0 || id.Length > 64 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException(source + " line " + number + ": the build ID is not hexadecimal.");
                    current = new CodePatch { Source = source, BuildId = id.ToUpperInvariant() };
                    patches.Add(current);
                    shift = 0; enabled = false;
                    continue;
                }
                if (line.StartsWith("@flag", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && parts[1].Equals("offset_shift", StringComparison.OrdinalIgnoreCase)) shift = ParseHex(parts[2], source, number);
                    continue;
                }
                if (line.Equals("@enabled", StringComparison.OrdinalIgnoreCase)) { enabled = true; continue; }
                if (line.Equals("@disabled", StringComparison.OrdinalIgnoreCase)) { enabled = false; continue; }
                if (line.Equals("@stop", StringComparison.OrdinalIgnoreCase)) break;
                if (line[0] == '@') continue;
                if (current == null) throw new InvalidDataException(source + " line " + number + ": a patch line comes before @nsobid-.");
                if (!enabled) continue;
                int space = line.IndexOfAny(new[] { ' ', '\t' });
                if (space < 0) throw new InvalidDataException(source + " line " + number + ": expected an offset and its bytes.");
                long offset = ParseHex(line.Substring(0, space), source, number) + shift - 0x100;
                if (offset < 0) throw new InvalidDataException(source + " line " + number + ": the offset falls inside the NSO header.");
                string value = line.Substring(space).Trim();
                byte[] bytes = value.StartsWith("\"", StringComparison.Ordinal) ? Unquote(value, source, number) : HexBytes(value, source, number);
                current.Edits.Add(new CodeEdit { Offset = offset, Bytes = bytes });
            }
            patches.RemoveAll(p => p.Edits.Count == 0);
            return patches;
        }

        static long ParseHex(string s, string source, int number)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            long v;
            if (!Int64.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) throw new InvalidDataException(source + " line " + number + ": " + s + " is not a hex number.");
            return v;
        }
        static byte[] HexBytes(string s, string source, int number)
        {
            s = s.Replace(" ", "").Replace("\t", "");
            if (s.Length == 0 || s.Length % 2 != 0 || !s.All(Uri.IsHexDigit)) throw new InvalidDataException(source + " line " + number + ": the bytes are not whole hex bytes.");
            var bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }
        static byte[] Unquote(string s, string source, int number)
        {
            int end = s.LastIndexOf('"');
            if (end <= 0) throw new InvalidDataException(source + " line " + number + ": the text has no closing quote.");
            var text = new StringBuilder();
            for (int i = 1; i < end; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < end)
                {
                    char e = s[++i];
                    text.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == '0' ? '\0' : e);
                }
                else text.Append(c);
            }
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        /// <summary>A module's build ID as 64 hex digits (the NSO header's 32 bytes).</summary>
        public static string BuildId(byte[] nso)
        {
            if (nso.Length < 0x100 || Encoding.ASCII.GetString(nso, 0, 4) != "NSO0") throw new InvalidDataException("Not an NSO module.");
            return BitConverter.ToString(nso, 0x40, 32).Replace("-", "");
        }

        /// <summary>
        /// A patch names this module when its ID is the start of the build ID, at least 16 digits of it: patch files give
        /// 32 digits or fewer (one 60 FPS patch drops the 32nd, a 0), the header holds 64 with zeros after the first 32.
        /// </summary>
        public static bool Matches(CodePatch patch, string buildId)
        {
            return patch.BuildId.Length >= 16 && buildId.ToUpperInvariant().StartsWith(patch.BuildId, StringComparison.Ordinal);
        }

        /// <summary>
        /// The module with the edits applied, written uncompressed with each segment's SHA-256 recomputed (the engine
        /// checks a hash only when its flag is set). Every edit must lie inside a segment, or the module is refused.
        /// </summary>
        public static byte[] Apply(byte[] nso, IEnumerable<CodeEdit> edits)
        {
            BuildId(nso);
            uint flags = U32(nso, 0x0C);
            var fileOff = new uint[3]; var memOff = new uint[3]; var size = new uint[3]; var segment = new byte[3][];
            for (int i = 0; i < 3; i++)
            {
                fileOff[i] = U32(nso, 0x10 + i * 0x10); memOff[i] = U32(nso, 0x14 + i * 0x10); size[i] = U32(nso, 0x18 + i * 0x10);
                uint stored = (flags & (1u << i)) != 0 ? U32(nso, 0x60 + i * 4) : size[i];
                if ((long)fileOff[i] + stored > nso.Length) throw new InvalidDataException("The module's " + SegmentName(i) + " segment lies outside the file.");
                segment[i] = (flags & (1u << i)) != 0 ? Lz4Block(nso, (int)fileOff[i], (int)stored, (int)size[i]) : Slice(nso, (int)fileOff[i], (int)size[i]);
            }
            foreach (var edit in edits)
            {
                int s = Enumerable.Range(0, 3).FirstOrDefault(i => edit.Offset >= memOff[i] && edit.Offset + edit.Bytes.Length <= (long)memOff[i] + size[i]);
                if (!(edit.Offset >= memOff[s] && edit.Offset + edit.Bytes.Length <= (long)memOff[s] + size[s]))
                    throw new InvalidDataException("A patch writes at 0x" + edit.Offset.ToString("X") + ", outside the module's code and data.");
                Buffer.BlockCopy(edit.Bytes, 0, segment[s], (int)(edit.Offset - memOff[s]), edit.Bytes.Length);
            }
            // the header, the module name where it was (inside the header's first 0x100 bytes or after them), then the
            // three segments one after another
            uint nameOff = U32(nso, 0x1C), nameSize = U32(nso, 0x2C);
            byte[] name = nameSize > 0 && (long)nameOff + nameSize <= nso.Length ? Slice(nso, (int)nameOff, (int)nameSize) : new byte[0];
            var output = new MemoryStream();
            var header = Slice(nso, 0, 0x100);
            uint at = 0x100;
            Put(header, 0x1C, at); Put(header, 0x2C, (uint)name.Length);
            at += (uint)name.Length;
            using (var sha = SHA256.Create())
                for (int i = 0; i < 3; i++)
                {
                    Put(header, 0x10 + i * 0x10, at);
                    Put(header, 0x60 + i * 4, size[i]);
                    if ((flags & (8u << i)) != 0) Buffer.BlockCopy(sha.ComputeHash(segment[i]), 0, header, 0xA0 + i * 0x20, 32);
                    at += size[i];
                }
            Put(header, 0x0C, flags & ~7u);
            output.Write(header, 0, header.Length);
            output.Write(name, 0, name.Length);
            for (int i = 0; i < 3; i++) output.Write(segment[i], 0, segment[i].Length);
            return output.ToArray();
        }

        static string SegmentName(int i) { return i == 0 ? "code" : i == 1 ? "read-only data" : "data"; }
        static uint U32(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
        static void Put(byte[] b, int at, uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, at, 4); }
        static byte[] Slice(byte[] b, int at, int n) { var r = new byte[n]; Buffer.BlockCopy(b, at, r, 0, n); return r; }

        /// <summary>An LZ4 block (the NSO segment compression) decoded to exactly size bytes.</summary>
        static byte[] Lz4Block(byte[] src, int at, int length, int size)
        {
            var dst = new byte[size];
            int i = at, end = at + length, o = 0;
            while (i < end)
            {
                int token = src[i++], literals = token >> 4;
                if (literals == 15) { int b; do { b = src[i++]; literals += b; } while (b == 255); }
                if (o + literals > size || i + literals > end) throw new InvalidDataException("The module's compressed code is damaged.");
                Buffer.BlockCopy(src, i, dst, o, literals); i += literals; o += literals;
                if (i >= end) break;
                int back = src[i] | src[i + 1] << 8; i += 2;
                int match = token & 15;
                if (match == 15) { int b; do { b = src[i++]; match += b; } while (b == 255); }
                match += 4;
                if (back == 0 || back > o || o + match > size) throw new InvalidDataException("The module's compressed code is damaged.");
                for (int k = 0; k < match; k++, o++) dst[o] = dst[o - back];
            }
            if (o != size) throw new InvalidDataException("The module's compressed code is damaged.");
            return dst;
        }

        /// <summary>
        /// Every .pchtxt under folder (and its subfolders) whose build ID names one of the program's modules in exefs is
        /// applied, and the patched modules are written to dataFolder/mods/PortForge code patches/exefs. Returns one line
        /// per applied patch file; an empty list when none matched (nothing is written). A patch file that doesn't
        /// parse is reported in the list and skipped.
        /// </summary>
        public static List<string> PrepareMods(string exefs, string folder, string dataFolder)
        {
            var report = new List<string>();
            if (String.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return report;
            var files = Directory.GetFiles(folder, "*.pchtxt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) return report;
            var patches = new List<CodePatch>();
            foreach (string file in files)
            {
                try { patches.AddRange(Parse(File.ReadAllText(file), Path.GetFileName(file))); }
                catch (InvalidDataException e) { report.Add("Skipped code patch " + Path.GetFileName(file) + ": " + e.Message); }
            }
            string target = Path.Combine(dataFolder, "mods", ModName, "exefs");
            foreach (string module in Modules)
            {
                string path = Path.Combine(exefs, module);
                if (!File.Exists(path)) continue;
                byte[] nso = File.ReadAllBytes(path);
                if (nso.Length < 0x100 || Encoding.ASCII.GetString(nso, 0, 4) != "NSO0") continue;
                string id = BuildId(nso);
                var matching = patches.Where(p => Matches(p, id)).ToList();
                if (matching.Count == 0) continue;
                byte[] patched = Apply(nso, matching.SelectMany(p => p.Edits));
                Directory.CreateDirectory(target);
                File.WriteAllBytes(Path.Combine(target, module), patched);
                foreach (var p in matching)
                    report.Add("Applied code patch " + p.Source + " to " + module + " (build " + id.Substring(0, 16) + ", " + p.Edits.Count + " change" + (p.Edits.Count == 1 ? "" : "s") + ").");
            }
            return report;
        }
    }
}

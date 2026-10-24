using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PortForge
{
    // One presented frame from the engine's frametime-trace.csv (Engine 1.18+).
    public sealed class FrameTraceRow
    {
        public long Frame; public double Seconds, Milliseconds; public bool Hitch; public string Cause = "";
        public Dictionary<string, double> Parts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class FrameTraceSummary
    {
        public int Frames, Hitches; public double TypicalMs, OnePercentLowFps, WorstMs, HitchSeconds, LengthSeconds;
        public List<KeyValuePair<string, int>> HitchCauses = new List<KeyValuePair<string, int>>();
        public List<FrameTraceRow> Worst = new List<FrameTraceRow>();
    }

    public static class FrameTrace
    {
        public const string FileName = "frametime-trace.csv";
        public static string PathFor(string gameExe)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe)), FileName);
        }

        public static List<FrameTraceRow> Parse(string text)
        {
            var rows = new List<FrameTraceRow>();
            var lines = text.Replace("\r", "").Split('\n');
            if (lines.Length == 0) return rows;
            var header = lines[0].Split(',');
            int frame = Array.IndexOf(header, "frame"), time = Array.IndexOf(header, "time_s"), ms = Array.IndexOf(header, "frame_ms"),
                hitch = Array.IndexOf(header, "hitch"), cause = Array.IndexOf(header, "cause");
            if (frame < 0 || ms < 0) throw new InvalidDataException("This is not a PortForge frame-time trace.");
            for (int i = 1; i < lines.Length; i++) {
                var f = lines[i].Split(',');
                if (f.Length != header.Length) continue; // a line still being written when the game closed
                var row = new FrameTraceRow();
                double d; long n;
                if (!Int64.TryParse(f[frame], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                row.Frame = n;
                if (!Double.TryParse(f[ms], NumberStyles.Float, CultureInfo.InvariantCulture, out d) ||
                    Double.IsNaN(d) || Double.IsInfinity(d) || d < 0) continue;
                row.Milliseconds = d;
                if (time >= 0 && Double.TryParse(f[time], NumberStyles.Float, CultureInfo.InvariantCulture, out d) &&
                    !Double.IsNaN(d) && !Double.IsInfinity(d) && d >= 0) row.Seconds = d;
                row.Hitch = hitch >= 0 && f[hitch] == "1";
                if (cause >= 0) row.Cause = f[cause];
                for (int c = 0; c < header.Length; c++)
                    if (header[c].EndsWith("_ms", StringComparison.Ordinal) && c != ms &&
                        Double.TryParse(f[c], NumberStyles.Float, CultureInfo.InvariantCulture, out d) &&
                        !Double.IsNaN(d) && !Double.IsInfinity(d) && d >= 0) row.Parts[header[c]] = d;
                rows.Add(row);
            }
            return rows;
        }

        public static FrameTraceSummary Summarize(IList<FrameTraceRow> rows)
        {
            var s = new FrameTraceSummary();
            s.Frames = rows.Count;
            if (rows.Count == 0) return s;
            var sorted = rows.Select(r => r.Milliseconds).OrderBy(x => x).ToArray();
            s.TypicalMs = sorted[sorted.Length / 2];
            double p99 = sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.99) - 1)];
            s.OnePercentLowFps = p99 > 0 ? 1000.0 / p99 : 0;
            s.WorstMs = sorted[sorted.Length - 1];
            s.LengthSeconds = rows[rows.Count - 1].Seconds;
            var hitches = rows.Where(r => r.Hitch).ToList();
            s.Hitches = hitches.Count;
            s.HitchSeconds = hitches.Sum(r => r.Milliseconds) / 1000.0;
            s.HitchCauses = hitches.GroupBy(r => r.Cause).Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .OrderByDescending(p => p.Value).ToList();
            s.Worst = rows.OrderByDescending(r => r.Milliseconds).Take(20).ToList();
            return s;
        }

        public static string Describe(FrameTraceRow row)
        {
            var parts = row.Parts.Where(p => p.Value > 0).OrderByDescending(p => p.Value)
                .Select(p => p.Key.Replace("_ms", "").Replace('_', ' ') + " " + p.Value.ToString("0", CultureInfo.InvariantCulture) + " ms");
            string detail = String.Join(", ", parts.ToArray());
            return row.Cause + (detail.Length > 0 ? " (" + detail + ")" : "");
        }
    }
}

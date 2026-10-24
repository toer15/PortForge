using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace PortForge
{
    // Shows a game's frame-time trace: every frame as a bar, hitches in red, and what the engine spent the worst ones on.
    public sealed class FrameTraceWindow : Form
    {
        private readonly Label status = new Label { AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), AccessibleName = "Trace status" };
        private readonly Label summary = new Label { AutoSize = true, MaximumSize = new Size(1100, 0), Font = new Font("Segoe UI", 10) };
        private readonly TextBox path = new TextBox { Width = 760, AccessibleName = "Trace file" };
        private readonly TracePlot plot = new TracePlot { Height = 260, Dock = DockStyle.Top };
        private readonly ListView worst = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, AccessibleName = "Slowest frames" };

        public FrameTraceWindow(string gameExe)
        {
            Text = "Frame-time trace"; Width = 1180; Height = 820; StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(17, 21, 30); ForeColor = Color.WhiteSmoke; Icon = AppIcon.Get();
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
            top.Controls.Add(status);
            top.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(1100, 0), Text = "Turn on \"Record frame-time trace\" in Settings, play the game, then close it and load its trace here. Each bar is one frame: green is normal, yellow is slower than usual, red is a hitch." });
            var row = new FlowLayoutPanel { AutoSize = true };
            row.Controls.Add(path);
            var browse = new ReadableButton { Text = "Browse...", AutoSize = true };
            var clear = new ReadableButton { Text = "Clear", AutoSize = true, AccessibleName = "Clear trace file" };
            var load = new ReadableButton { Text = "Load", AutoSize = true };
            row.Controls.Add(browse); row.Controls.Add(clear); row.Controls.Add(load);
            top.Controls.Add(row);
            top.Controls.Add(summary);
            worst.Columns.Add("Time", 90); worst.Columns.Add("Frame time", 110); worst.Columns.Add("What the engine spent it on", 900);
            var fill = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16) };
            fill.Controls.Add(worst); fill.Controls.Add(plot);
            Controls.Add(fill); Controls.Add(top);
            browse.Click += delegate {
                using (var d = new OpenFileDialog { Filter = "Frame-time trace|frametime-trace.csv;*.csv" }) if (d.ShowDialog(this) == DialogResult.OK) { path.Text = d.FileName; LoadTrace(); }
            };
            clear.Click += delegate { path.Text = ""; plot.Rows = null; plot.Invalidate(); worst.Items.Clear(); summary.Text = ""; SetStatus("READY - Choose a trace file.", false); };
            load.Click += delegate { LoadTrace(); };
            path.Text = String.IsNullOrEmpty(gameExe) ? "" : FrameTrace.PathFor(gameExe);
            if (path.Text.Length > 0 && File.Exists(path.Text)) LoadTrace();
            else SetStatus("READY - No trace yet for this game. Turn on recording in Settings and play.", false);
        }

        private void SetStatus(string text, bool error) { status.Text = text; status.ForeColor = error ? Color.FromArgb(255, 120, 120) : Color.FromArgb(105, 226, 183); }

        private void LoadTrace()
        {
            try {
                var rows = FrameTrace.Parse(File.ReadAllText(path.Text));
                var s = FrameTrace.Summarize(rows);
                plot.Rows = rows; plot.Invalidate();
                worst.Items.Clear();
                foreach (var r in s.Worst) {
                    var item = new ListViewItem(r.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    item.SubItems.Add(r.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
                    item.SubItems.Add(FrameTrace.Describe(r));
                    worst.Items.Add(item);
                }
                var causes = new List<string>();
                foreach (var c in s.HitchCauses) causes.Add(c.Key + " " + c.Value);
                summary.Text = String.Format(CultureInfo.InvariantCulture,
                    "{0} frames over {1:0} s. Typical frame {2:0.0} ms ({3:0} FPS), 1% low {4:0.0} FPS, worst {5:0} ms. {6} hitches, {7:0.0} s in all{8}",
                    s.Frames, s.LengthSeconds, s.TypicalMs, s.TypicalMs > 0 ? 1000 / s.TypicalMs : 0, s.OnePercentLowFps, s.WorstMs, s.Hitches, s.HitchSeconds,
                    causes.Count > 0 ? ": " + String.Join(", ", causes.ToArray()) + "." : ".");
                SetStatus(s.Hitches == 0 ? "DONE - No hitches in this trace." : "DONE - " + s.Hitches + " hitches found. The slowest are listed below.", false);
            } catch (Exception e) { SetStatus("ERROR - " + e.Message, true); }
        }

        private sealed class TracePlot : Control
        {
            public List<FrameTraceRow> Rows;
            public TracePlot() { DoubleBuffered = true; BackColor = Color.FromArgb(24, 28, 38); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; int w = ClientSize.Width, h = ClientSize.Height - 18;
                const double range = 100;
                using (var grid = new Pen(Color.FromArgb(80, 80, 80)))
                using (var text = new SolidBrush(Color.Silver)) {
                    foreach (var ms in new[] { 16.7, 33.3, 50.0 }) {
                        int y = h - (int)(h * ms / range);
                        g.DrawLine(grid, 0, y, w, y);
                        g.DrawString(ms.ToString("0.#", CultureInfo.InvariantCulture) + " ms", Font, text, 2, y - 14);
                    }
                    if (Rows == null || Rows.Count == 0) return;
                    double typical = FrameTrace.Summarize(Rows).TypicalMs;
                    // each pixel column shows the slowest frame among those it covers
                    for (int x = 0; x < w; x++) {
                        int a = (int)((long)x * Rows.Count / w), b = Math.Max(a + 1, (int)((long)(x + 1) * Rows.Count / w));
                        FrameTraceRow most = null;
                        for (int i = a; i < b && i < Rows.Count; i++) if (most == null || Rows[i].Milliseconds > most.Milliseconds) most = Rows[i];
                        if (most == null) continue;
                        int bh = Math.Max(1, (int)(h * Math.Min(most.Milliseconds, range) / range));
                        var color = most.Hitch ? Color.FromArgb(255, 60, 60) : most.Milliseconds > 1.5 * typical ? Color.FromArgb(255, 208, 32) : Color.FromArgb(64, 208, 96);
                        using (var pen = new Pen(color)) g.DrawLine(pen, x, h, x, h - bh);
                    }
                    g.DrawString("0 s", Font, text, 0, h + 2);
                    string end = Rows[Rows.Count - 1].Seconds.ToString("0", CultureInfo.InvariantCulture) + " s";
                    g.DrawString(end, Font, text, w - g.MeasureString(end, Font).Width, h + 2);
                }
            }
        }
    }
}

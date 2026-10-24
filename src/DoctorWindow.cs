using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PortForge
{
    /// <summary>The live doctor's report: what it saw while games ran and what it fixed.</summary>
    public sealed class DoctorWindow : Form
    {
        protected override CreateParams CreateParams { get { return SmoothPaint.Composited(base.CreateParams); } }
        private static readonly Color Bg = Color.FromArgb(17, 21, 30), Surface = Color.FromArgb(28, 34, 46), Mint = Color.FromArgb(105, 226, 183), Muted = Color.FromArgb(173, 185, 203);
        private readonly TextBox events = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Surface, ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Doctor events", Font = new Font("Segoe UI", 10) };
        private readonly List<DoctorEvent> source;
        public string ShownText { get { return events.Text; } }

        public DoctorWindow(List<DoctorEvent> history, LiveDoctor doctor)
        {
            source = history;
            Text = "PortForge | AI Doctor"; AppIcon.Apply(this); ClientSize = new Size(860, 560); MinimumSize = new Size(640, 420); StartPosition = FormStartPosition.CenterParent;
            BackColor = Bg; ForeColor = Color.WhiteSmoke; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.Controls.Add(new Label { Text = "AI Doctor", AutoSize = true, Font = new Font("Segoe UI", 20), ForeColor = Color.White }, 0, 0);
            var about = new Label { AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9), MaximumSize = new Size(800, 0),
                Text = "Watching: " + (doctor == null || doctor.Games.Length == 0 ? "no games yet (open one in Settings / Game tools, or convert one)" : String.Join(", ", doctor.Games.Select(g => System.IO.Path.GetFileNameWithoutExtension(g)))) +
                       ". While a game runs, the doctor reads its log and explains each slowdown. It never changes a running game. When the game closes, it puts the PortForge Engine under it (the stutter fix) and applies the supported repairs, with the original engine, settings and save backed up first. This works on any PC, with no AI account. If Codex is installed and signed in, it also gives an extra AI review of new problems." + (LiveDoctor.AiStatus.Length > 0 ? " " + LiveDoctor.AiStatus : "") };
            root.Controls.Add(about, 0, 1);
            root.Controls.Add(events, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
            var refresh = Button("Refresh", true); refresh.Click += delegate { Fill(); };
            var clear = Button("Clear", false); clear.Click += delegate { lock (source) source.Clear(); Fill(); };
            var close = Button("Close", false); close.Click += delegate { Close(); };
            actions.Controls.AddRange(new Control[] { refresh, clear, close });
            root.Controls.Add(actions, 0, 3); Controls.Add(root);
            Fill();
        }

        public void Fill()
        {
            DoctorEvent[] list; lock (source) list = source.ToArray();
            events.Text = list.Length == 0 ? "Nothing to report yet. Start a game and the doctor will report here." : String.Join(Environment.NewLine, list.Reverse().Select(e => (e.Fixed ? "FIXED  " : e.Important ? "NOTICE " : "       ") + e));
        }

        private static Button Button(string text, bool primary)
        {
            var b = new ReadableButton { Text = text, AccessibleName = text, Size = new Size(130, 34), FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Surface, ForeColor = primary ? Bg : Color.WhiteSmoke, Margin = new Padding(0, 0, 8, 0) };
            b.FlatAppearance.BorderSize = 0; return b;
        }
    }
}

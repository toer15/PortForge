using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortForge
{
    /// <summary>
    /// The Controllers window: which controller the player uses, how a game's buttons sit on it, and a live drawing of it
    /// as the engine reads it (buttons light up and sticks and triggers move on a fixed controller picture).
    /// </summary>
    public sealed class ControllerWindow : Form
    {
        protected override CreateParams CreateParams { get { return SmoothPaint.Composited(base.CreateParams); } }
        private static readonly Color Bg = Color.FromArgb(17, 21, 30), Surface = Color.FromArgb(28, 34, 46), Mint = Color.FromArgb(105, 226, 183), Muted = Color.FromArgb(173, 185, 203);
        private readonly ComboBox kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, AccessibleName = "Your controller" };
        private readonly ComboBox layout = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, AccessibleName = "Buttons in games" };
        private readonly CheckBox pair = new CheckBox { Text = "Use two Joy-Cons together as one controller", AutoSize = true, AccessibleName = "Pair Joy-Cons" };
        private readonly Label status = new Label { Dock = DockStyle.Fill, ForeColor = Mint, Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label chart = new Label { AutoSize = true, ForeColor = Color.WhiteSmoke, Font = new Font("Consolas", 10) };
        private readonly Label result = new Label { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(940, 0) };
        private readonly ControllerView view = new ControllerView { Dock = DockStyle.Fill };
        private readonly Button save = Button("Save for all my games", true);
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly ControllerMonitor monitor = new ControllerMonitor();
        private readonly Func<IEnumerable<string>> knownGames;
        private ControllerSettings settings = ControllerSettings.Load();
        private ControllerKind detected = ControllerKind.Generic;
        private bool loading, saving;
        private string lastStatus;

        public ControllerWindow() : this(null, ControllerMonitor.EnginePath()) { }
        public ControllerWindow(Func<IEnumerable<string>> games, string engine)
        {
            knownGames = games ?? FindGames;
            Text = "PortForge | Controllers"; AppIcon.Apply(this); ClientSize = new Size(1000, 860); MinimumSize = new Size(900, 780); StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; BackColor = Bg; ForeColor = Color.WhiteSmoke;
            kind.Items.AddRange(ControllerSettings.KindNames);
            layout.Items.AddRange(new object[] { "By position, like on a Switch (recommended)", "By the letters printed on my controller" });

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(22, 16, 22, 16) };
            foreach (int h in new[] { 46, 48, 112 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 44, 150, 60 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            root.Controls.Add(new Label { Text = "Controllers", Font = new Font(Font.FontFamily, 22), AutoSize = true }, 0, 0);

            var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(14, 0, 14, 0), Margin = new Padding(0, 0, 0, 8) };
            card.Controls.Add(status); root.Controls.Add(card, 0, 1);

            var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = new Padding(0) };
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 345)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddChoice(choices, 0, "Your controller", kind, delegate { kind.SelectedIndex = 0; });
            AddChoice(choices, 1, "Buttons in games", layout, delegate { layout.SelectedIndex = 0; });
            choices.Controls.Add(pair, 1, 2);
            root.Controls.Add(choices, 0, 2);

            var stage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 27, 38), Margin = new Padding(0, 4, 0, 4) };
            stage.Controls.Add(view); root.Controls.Add(stage, 0, 3);

            var viewActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            viewActions.Controls.Add(new Label { Text = "Press buttons or move the sticks to see your input. The controller picture stays still.", ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            root.Controls.Add(viewActions, 0, 4);

            var chartBox = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
            var chartTitle = new Label { Text = "When a game shows a Switch button, press this on your controller:", AutoSize = true, ForeColor = Muted, Location = new Point(0, 0) };
            chart.Location = new Point(0, 24); chartBox.Controls.Add(chartTitle); chartBox.Controls.Add(chart);
            root.Controls.Add(chartBox, 0, 5);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            save.Size = new Size(210, 40); actions.Controls.Add(save); result.Margin = new Padding(12, 10, 0, 0); actions.Controls.Add(result);
            root.Controls.Add(actions, 0, 6);
            Controls.Add(root);

            loading = true;
            kind.SelectedIndex = (int)settings.Kind; layout.SelectedIndex = (int)settings.Layout; pair.Checked = settings.PairJoyCons;
            loading = false;
            kind.SelectedIndexChanged += delegate { Changed(); }; layout.SelectedIndexChanged += delegate { Changed(); }; pair.CheckedChanged += delegate { Changed(); };
            save.Click += async delegate { await SaveAll(); };
            timer.Tick += delegate { Tick(); };
            Shown += delegate { try { monitor.Start(engine); } catch (Exception error) { status.Text = "ENGINE PROBLEM — " + error.Message; } timer.Start(); };
            FormClosed += delegate { timer.Stop(); monitor.Dispose(); };
            Changed(true);
        }

        private void AddChoice(TableLayoutPanel table, int row, string text, ComboBox box, EventHandler clear)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            table.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, row);
            table.Controls.Add(box, 1, row);
            var reset = Button("Clear", false); reset.Size = new Size(80, 30); reset.AccessibleName = "Clear " + text.ToLowerInvariant(); reset.Click += clear;
            table.Controls.Add(reset, 2, row);
        }

        private ControllerKind ShownKind { get { var k = (ControllerKind)Math.Max(0, kind.SelectedIndex); return k == ControllerKind.Automatic ? detected : k; } }

        private void Changed(bool initial = false)
        {
            if (loading) return;
            var k = ShownKind;
            bool letters = k == ControllerKind.Xbox || k == ControllerKind.Generic;
            layout.Enabled = letters;
            if (!letters && layout.SelectedIndex != 0) { loading = true; layout.SelectedIndex = 0; loading = false; }
            pair.Enabled = k == ControllerKind.JoyConPair || k == ControllerKind.JoyConLeft || k == ControllerKind.JoyConRight || kind.SelectedIndex == 0;
            var rows = ControllerSettings.GameButtonChart(k, (ButtonLayout)Math.Max(0, layout.SelectedIndex));
            chart.Text = String.Join("\r\n", Enumerable.Range(0, (rows.Length + 1) / 2).Select(i =>
                Row(rows[i]) + (i + (rows.Length + 1) / 2 < rows.Length ? "      " + Row(rows[i + (rows.Length + 1) / 2]) : "")));
            view.Kind = k;
            if (!initial) result.Text = "Not saved yet. Save to use this in your games.";
        }
        private static string Row(string[] r) { return ("Game " + r[0]).PadRight(24) + "-> " + r[1].PadRight(14); }

        private void Tick()
        {
            var pads = monitor.Snapshot();
            var first = pads.FirstOrDefault();
            string text;
            if (monitor.Error != null) text = "ENGINE PROBLEM — " + monitor.Error;
            else if (!monitor.Ready) text = "STARTING — Asking the PortForge Engine for your controllers...";
            else if (first == null) text = "NO CONTROLLER — Connect a controller by USB or Bluetooth. It appears here at once.";
            else
            {
                var found = ControllerSettings.FromEngineType(first.Type, first.Name);
                if (found != detected) { detected = found; Changed(true); }
                bool motion = first.Motion.Any(m => m.Valid);
                text = "CONNECTED — " + first.Name + (motion ? " (motion sensors on)" : " (no motion sensors)") + (pads.Count > 1 ? " and " + (pads.Count - 1) + " more" : "");
            }
            if (text != lastStatus) { status.Text = text; lastStatus = text; }
            view.State = first;
            view.Invalidate();
        }

        private async Task SaveAll()
        {
            if (saving) return;
            saving = true; save.Enabled = false; result.ForeColor = Muted; result.Text = "Saving and applying to your converted games...";
            try
            {
                settings = new ControllerSettings { Kind = (ControllerKind)kind.SelectedIndex, Layout = (ButtonLayout)layout.SelectedIndex, PairJoyCons = pair.Checked };
                settings.Save();
                var games = knownGames().ToList();
                var report = await Task.Run(() => settings.ApplyTo(games, CancellationToken.None));
                int applied = report.Count(r => r.EndsWith(": applied") || r.EndsWith(": already set"));
                result.ForeColor = Mint;
                result.Text = "SAVED — " + (games.Count == 0 ? "New games get it when you convert them." : applied + " of " + report.Count + " game(s) use it now. " + String.Join("; ", report.Take(4)) + (report.Count > 4 ? "; ..." : ""));
            }
            catch (Exception error) { result.ForeColor = Color.FromArgb(255, 140, 140); result.Text = "NOT SAVED — " + error.Message; }
            finally { saving = false; save.Enabled = true; }
        }

        private static IEnumerable<string> FindGames()
        {
            var games = LiveDoctor.FindConvertedGames(LiveDoctor.DefaultDiscoveryRoots(AppDomain.CurrentDomain.BaseDirectory));
            if (LiveDoctor.Current != null) games.AddRange(LiveDoctor.Current.Games);
            string last = GameSettingsWindow.RememberedGame(); if (last != null) games.Add(last);
            return games;
        }
        private static Button Button(string text, bool primary)
        {
            var b = new ReadableButton { Text = text, AccessibleName = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Surface, ForeColor = primary ? Bg : Color.WhiteSmoke, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0; return b;
        }
    }

    /// <summary>Draws a fixed controller picture with live buttons, sticks and triggers.</summary>
    public sealed class ControllerView : Control
    {
        public ControllerKind Kind = ControllerKind.Generic;
        public ControllerState State;
        private static readonly Color Lit = Color.FromArgb(105, 226, 183), Ink = Color.FromArgb(17, 21, 30);

        public ControllerView() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); BackColor = Color.FromArgb(22, 27, 38); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(BackColor);
            float scale = Math.Min(Width / 430f, Height / 300f), cx = Width / 2f, cy = Height / 2f + 6 * scale;
            var s = State ?? new ControllerState();
            if (Kind == ControllerKind.JoyConPair)
            {
                Draw(g, cx - 70 * scale, cy, scale, delegate { JoyCon(g, s, false); });
                Draw(g, cx + 70 * scale, cy, scale, delegate { JoyCon(g, s, true); });
            }
            else if (Kind == ControllerKind.JoyConLeft) Draw(g, cx, cy, scale, delegate { JoyCon(g, s, false); });
            else if (Kind == ControllerKind.JoyConRight) Draw(g, cx, cy, scale, delegate { JoyCon(g, s, true); });
            else if (Kind == ControllerKind.DualSense || Kind == ControllerKind.DualShock4) Draw(g, cx, cy, scale, delegate { PlayStation(g, s, Kind == ControllerKind.DualSense); });
            else Draw(g, cx, cy, scale, delegate { Offset(g, s, Kind); });
            if (State == null)
                using (var f = new Font("Segoe UI", 11)) using (var b = new SolidBrush(Color.FromArgb(173, 185, 203)))
                    g.DrawString("Connect a controller to see it live", f, b, 12, Height - 30);
        }

        /// <summary>Keep the shell in place while individual controls show their input.</summary>
        private static void Draw(Graphics g, float cx, float cy, float scale, Action paint)
        {
            var saved = g.Save();
            using (var transform = new Matrix(scale, 0, 0, scale, cx, cy)) g.Transform = transform;
            paint();
            g.Restore(saved);
        }

        // ---- Xbox, Switch Pro and other controllers: left stick high, d-pad low, face buttons high, right stick low ----
        private static void Offset(Graphics g, ControllerState s, ControllerKind kind)
        {
            bool nintendo = kind == ControllerKind.SwitchPro, xbox = kind == ControllerKind.Xbox;
            Shoulders(g, s, nintendo ? new[] { "ZL", "ZR", "L", "R" } : new[] { "LT", "RT", "LB", "RB" });
            Body(g, nintendo ? Color.FromArgb(52, 54, 60) : xbox ? Color.FromArgb(46, 50, 58) : Color.FromArgb(60, 64, 76));
            Stick(g, -95, -28, s.LeftX, s.LeftY, s.Held(ControllerButton.LeftStick));
            Dpad(g, -48, 30, s);
            Stick(g, 48, 30, s.RightX, s.RightY, s.Held(ControllerButton.RightStick));
            string[] face = ControllerSettings.FaceLabels(kind);
            Color[] colors = xbox ? new[] { Color.FromArgb(76, 175, 80), Color.FromArgb(229, 57, 53), Color.FromArgb(30, 136, 229), Color.FromArgb(253, 216, 53) }
                                  : new[] { Color.WhiteSmoke, Color.WhiteSmoke, Color.WhiteSmoke, Color.WhiteSmoke };
            Face(g, 95, -28, s, face, colors);
            Small(g, -30, -45, nintendo ? "-" : "View", s.Held(ControllerButton.Back));
            Small(g, 30, -45, nintendo ? "+" : "Menu", s.Held(ControllerButton.Start));
            Round(g, 0, -66, 11, nintendo ? "Home" : "", s.Held(ControllerButton.Guide), xbox ? Color.FromArgb(120, 200, 120) : Color.FromArgb(90, 94, 104));
            Small(g, 0, -20, nintendo ? "Capture" : "Share", s.Held(ControllerButton.Misc1));
            Paddles(g, s);
        }
        // ---- DualSense and DualShock 4: d-pad and face buttons high, sticks low and side by side ----
        private static void PlayStation(Graphics g, ControllerState s, bool dualSense)
        {
            Shoulders(g, s, new[] { "L2", "R2", "L1", "R1" });
            Body(g, dualSense ? Color.FromArgb(232, 234, 240) : Color.FromArgb(36, 40, 52));
            var pad = RoundRect(-52, -78, 104, 52, 10);
            using (var b = new SolidBrush(s.Held(ControllerButton.Touchpad) ? Lit : dualSense ? Color.FromArgb(205, 208, 216) : Color.FromArgb(52, 58, 74))) g.FillPath(b, pad);
            using (var p = new Pen(Color.FromArgb(120, 0, 0, 0), 1.5f)) g.DrawPath(p, pad);
            DrawLabel(g, "Touchpad", 0, -52, 8, dualSense ? Color.FromArgb(90, 94, 104) : Color.FromArgb(160, 170, 190));
            if (!dualSense) using (var b = new SolidBrush(Color.FromArgb(70, 140, 255))) g.FillRectangle(b, -40, -84, 80, 4); // light bar
            Dpad(g, -98, -32, s);
            Symbols(g, 98, -32, s);
            Stick(g, -46, 30, s.LeftX, s.LeftY, s.Held(ControllerButton.LeftStick));
            Stick(g, 46, 30, s.RightX, s.RightY, s.Held(ControllerButton.RightStick));
            Small(g, -66, -70, dualSense ? "Create" : "Share", s.Held(ControllerButton.Back));
            Small(g, 66, -70, "Options", s.Held(ControllerButton.Start));
            Round(g, 0, 14, 9, "PS", s.Held(ControllerButton.Guide), Color.FromArgb(40, 44, 54));
            if (dualSense) Small(g, 0, 34, "Mic", s.Held(ControllerButton.Misc1));
            Paddles(g, s);
        }
        // ---- a Joy-Con, upright; the pair draws both, each with its own half of the controls ----
        private static void JoyCon(Graphics g, ControllerState s, bool right)
        {
            Color shell = right ? Color.FromArgb(255, 78, 60) : Color.FromArgb(0, 174, 239);

            using (var b = new SolidBrush(Color.FromArgb(70, 72, 80))) g.FillRectangle(b, right ? -38 : 26, -112, 12, 224); // rail
            var body = new GraphicsPath();
            body.AddArc(right ? -30 : -40, -118, 70, 70, right ? 270 : 180, 90); body.AddArc(right ? -30 : -40, 48, 70, 70, right ? 0 : 90, 90);
            body.AddLine(right ? -30 : 30, 118, right ? -30 : 30, -118); body.CloseFigure();
            using (var b = new SolidBrush(shell)) g.FillPath(b, body);
            using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 2)) g.DrawPath(p, body);
            int sx = right ? 4 : -4;
            Bar(g, sx - 26, -128, 52, 9, right ? "R" : "L", s.Held(right ? ControllerButton.RightShoulder : ControllerButton.LeftShoulder), 0);
            Bar(g, sx - 26, -142, 52, 11, right ? "ZR" : "ZL", false, right ? s.RightTrigger : s.LeftTrigger);
            if (!right)
            {
                Stick(g, sx, -52, s.LeftX, s.LeftY, s.Held(ControllerButton.LeftStick));
                Round(g, sx, -6, 10, "", s.Held(ControllerButton.DpadUp), Color.FromArgb(40, 42, 48));
                Round(g, sx, 34, 10, "", s.Held(ControllerButton.DpadDown), Color.FromArgb(40, 42, 48));
                Round(g, sx - 20, 14, 10, "", s.Held(ControllerButton.DpadLeft), Color.FromArgb(40, 42, 48));
                Round(g, sx + 20, 14, 10, "", s.Held(ControllerButton.DpadRight), Color.FromArgb(40, 42, 48));
                Small(g, sx + 16, -94, "-", s.Held(ControllerButton.Back));
                Small(g, sx + 8, 74, "Capture", s.Held(ControllerButton.Misc1));
                Bar(g, 26, -40, 12, 22, "", s.Held(ControllerButton.LeftPaddle1), 0); Bar(g, 26, 20, 12, 22, "", s.Held(ControllerButton.LeftPaddle2), 0);
            }
            else
            {
                Face(g, sx, -52, s, ControllerSettings.FaceLabels(ControllerKind.JoyConRight), new[] { Color.WhiteSmoke, Color.WhiteSmoke, Color.WhiteSmoke, Color.WhiteSmoke });
                Stick(g, sx, 18, s.RightX, s.RightY, s.Held(ControllerButton.RightStick));
                Small(g, sx - 16, -94, "+", s.Held(ControllerButton.Start));
                Round(g, sx - 8, 74, 9, "Home", s.Held(ControllerButton.Guide), Color.FromArgb(40, 42, 48));
                Bar(g, -38, -40, 12, 22, "", s.Held(ControllerButton.RightPaddle1), 0); Bar(g, -38, 20, 12, 22, "", s.Held(ControllerButton.RightPaddle2), 0);
            }

        }

        private static void Body(Graphics g, Color color)
        {
            var path = new GraphicsPath { FillMode = FillMode.Winding };
            path.AddPath(RoundRect(-150, -88, 300, 118, 46), false);
            path.AddEllipse(-162, -40, 104, 150); path.AddEllipse(58, -40, 104, 150);
            using (var b = new SolidBrush(color)) g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f)) { g.DrawEllipse(p, -162, -40, 104, 150); g.DrawEllipse(p, 58, -40, 104, 150); }
        }
        private static void Shoulders(Graphics g, ControllerState s, string[] names)
        {
            Bar(g, -138, -118, 84, 16, names[0], false, s.LeftTrigger);
            Bar(g, 54, -118, 84, 16, names[1], false, s.RightTrigger);
            Bar(g, -142, -100, 92, 12, names[2], s.Held(ControllerButton.LeftShoulder), 0);
            Bar(g, 50, -100, 92, 12, names[3], s.Held(ControllerButton.RightShoulder), 0);
        }
        private static void Paddles(Graphics g, ControllerState s)
        {
            // back paddles (Elite, Edge, Pro grips): only drawn while pressed, beside the grips
            ControllerButton[] b = { ControllerButton.LeftPaddle1, ControllerButton.LeftPaddle2, ControllerButton.RightPaddle1, ControllerButton.RightPaddle2 };
            string[] n = { "Back L1", "Back L2", "Back R1", "Back R2" };
            for (int i = 0; i < 4; i++) if (s.Held(b[i])) Bar(g, i < 2 ? -196 : 150, 40 + (i % 2) * 22, 46, 16, n[i], true, 0);
        }
        /// <summary>A shoulder button (held) or a trigger (pull 0 to 32767, filled from the bottom).</summary>
        private static void Bar(Graphics g, float x, float y, float w, float h, string label, bool held, int pull)
        {
            var path = RoundRect(x, y, w, h, Math.Min(h, 8));
            using (var b = new SolidBrush(held ? Lit : Color.FromArgb(70, 76, 90))) g.FillPath(b, path);
            if (pull > 0)
            {
                float f = Math.Min(1, pull / 32767f);
                var clip = g.Clip; g.SetClip(path, CombineMode.Intersect);
                using (var b = new SolidBrush(Lit)) g.FillRectangle(b, x, y + h * (1 - f), w, h * f);
                g.Clip = clip;
            }
            if (label.Length > 0) DrawLabel(g, label, x + w / 2, y + h / 2, Math.Min(8, h * 0.6f), held || pull > 16000 ? Ink : Color.WhiteSmoke);
        }
        private static void Stick(Graphics g, float x, float y, int ax, int ay, bool pressed)
        {
            using (var b = new SolidBrush(Color.FromArgb(28, 30, 36))) g.FillEllipse(b, x - 25, y - 25, 50, 50);
            float dx = ax / 32767f * 13, dy = ay / 32767f * 13;
            using (var b = new SolidBrush(pressed ? Lit : Color.FromArgb(78, 82, 94))) g.FillEllipse(b, x + dx - 16, y + dy - 16, 32, 32);
            using (var p = new Pen(Color.FromArgb(110, 255, 255, 255), 1.5f)) g.DrawEllipse(p, x + dx - 11, y + dy - 11, 22, 22);
            if (Math.Abs(ax) > 2500 || Math.Abs(ay) > 2500) using (var p = new Pen(Lit, 2)) g.DrawLine(p, x, y, x + dx, y + dy);
        }
        private static void Dpad(Graphics g, float x, float y, ControllerState s)
        {
            Arm(g, x - 7, y - 25, 14, 18, s.Held(ControllerButton.DpadUp)); Arm(g, x - 7, y + 7, 14, 18, s.Held(ControllerButton.DpadDown));
            Arm(g, x - 25, y - 7, 18, 14, s.Held(ControllerButton.DpadLeft)); Arm(g, x + 7, y - 7, 18, 14, s.Held(ControllerButton.DpadRight));
            using (var b = new SolidBrush(Color.FromArgb(40, 42, 50))) g.FillRectangle(b, x - 7, y - 7, 14, 14);
        }
        private static void Arm(Graphics g, float x, float y, float w, float h, bool held) { using (var b = new SolidBrush(held ? Lit : Color.FromArgb(40, 42, 50))) g.FillPath(b, RoundRect(x, y, w, h, 3)); }
        /// <summary>Face buttons by position: south, east, west, north, with the controller's own letters.</summary>
        private static void Face(Graphics g, float x, float y, ControllerState s, string[] labels, Color[] colors)
        {
            Round(g, x, y + 21, 12, labels[0], s.Held(ControllerButton.South), Color.FromArgb(34, 36, 42), colors[0]);
            Round(g, x + 21, y, 12, labels[1], s.Held(ControllerButton.East), Color.FromArgb(34, 36, 42), colors[1]);
            Round(g, x - 21, y, 12, labels[2], s.Held(ControllerButton.West), Color.FromArgb(34, 36, 42), colors[2]);
            Round(g, x, y - 21, 12, labels[3], s.Held(ControllerButton.North), Color.FromArgb(34, 36, 42), colors[3]);
        }
        /// <summary>PlayStation's face buttons: cross, circle, square, triangle, drawn as their shapes.</summary>
        private static void Symbols(Graphics g, float x, float y, ControllerState s)
        {
            Shape(g, x, y + 21, s.Held(ControllerButton.South), 0, Color.FromArgb(124, 178, 232));
            Shape(g, x + 21, y, s.Held(ControllerButton.East), 1, Color.FromArgb(255, 102, 102));
            Shape(g, x - 21, y, s.Held(ControllerButton.West), 2, Color.FromArgb(255, 105, 248));
            Shape(g, x, y - 21, s.Held(ControllerButton.North), 3, Color.FromArgb(64, 226, 160));
        }
        private static void Shape(Graphics g, float x, float y, bool held, int which, Color color)
        {
            using (var b = new SolidBrush(held ? Lit : Color.FromArgb(34, 36, 42))) g.FillEllipse(b, x - 12, y - 12, 24, 24);
            using (var p = new Pen(held ? Ink : color, 2))
            {
                if (which == 0) { g.DrawLine(p, x - 5, y - 5, x + 5, y + 5); g.DrawLine(p, x - 5, y + 5, x + 5, y - 5); }
                else if (which == 1) g.DrawEllipse(p, x - 6, y - 6, 12, 12);
                else if (which == 2) g.DrawRectangle(p, x - 5, y - 5, 10, 10);
                else g.DrawPolygon(p, new[] { new PointF(x, y - 6), new PointF(x + 6, y + 5), new PointF(x - 6, y + 5) });
            }
        }
        private static void Round(Graphics g, float x, float y, float r, string label, bool held, Color fill) { Round(g, x, y, r, label, held, fill, Color.WhiteSmoke); }
        private static void Round(Graphics g, float x, float y, float r, string label, bool held, Color fill, Color text)
        {
            using (var b = new SolidBrush(held ? Lit : fill)) g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
            using (var p = new Pen(Color.FromArgb(60, 255, 255, 255), 1)) g.DrawEllipse(p, x - r, y - r, 2 * r, 2 * r);
            if (label.Length > 0) DrawLabel(g, label, x, y, label.Length > 2 ? 5.5f : 9, held ? Ink : text);
        }
        private static void Small(Graphics g, float x, float y, string label, bool held)
        {
            var path = RoundRect(x - 13, y - 6, 26, 12, 6);
            using (var b = new SolidBrush(held ? Lit : Color.FromArgb(40, 42, 50))) g.FillPath(b, path);
            DrawLabel(g, label, x, y, label.Length > 2 ? 5 : 8, held ? Ink : Color.WhiteSmoke);
        }
        private static void DrawLabel(Graphics g, string text, float x, float y, float size, Color color)
        {
            using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point)) using (var b = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, f, b, x, y, format);
        }
        private static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath(); float d = Math.Min(2 * r, Math.Min(w, h));
            p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90); p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }
    }
}

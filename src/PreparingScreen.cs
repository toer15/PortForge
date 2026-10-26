using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PortForge
{
    /// <summary>
    /// A game's very first launch: before it, PortForge plays the game's opening once, hidden, so the shaders it needs
    /// are recorded (GameOptimization.WarmCaches). At every launch after, the engine (1.37 and later) builds what was
    /// recorded behind its own screen before the game goes on, so the first real play doesn't stutter on them.
    /// </summary>
    public static class FirstLaunch
    {
        public const string MarkerName = "PortForge-first-launch.txt";
        /// <summary>The saved global settings when they apply to every game, else null.</summary>
        public static GlobalSettings Global()
        {
            return GlobalSettings.LoadEnabledOrNull();
        }
        /// <summary>The engine's record of the game's shaders: shader_cache\&lt;game&gt;.vkshaders beside the exe, or
        /// &lt;game&gt;.true-res.vkshaders when the next launch draws at a true resolution above the game's own (Engine 1.41
        /// translates texel fetches differently there and keeps those shaders apart).</summary>
        public static string RecordPath(string exe)
        {
            string full = Path.GetFullPath(exe);
            var g = Global();
            string mode = g != null && g.TrueResolutionActive ? ".true-res" : "";
            return Path.Combine(Path.GetDirectoryName(full), "shader_cache", Path.GetFileNameWithoutExtension(full) + mode + ".vkshaders");
        }
        /// <summary>The engine's shader translation as ENGINE.txt names it (shaders=, Engine 1.41 and later), or null.</summary>
        public static string EngineShaders(string exe)
        {
            try
            {
                string info = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "ENGINE.txt");
                if (!File.Exists(info)) return null;
                foreach (var line in File.ReadLines(info))
                    if (line.StartsWith("shaders=", StringComparison.Ordinal) && line.Length > 8) return line.Substring(8).Trim();
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        /// <summary>The translation a record was written by: "shaders=" in its header's key, or null.</summary>
        public static string RecordShaders(string record)
        {
            try
            {
                var buffer = new byte[2048];
                int n;
                using (var f = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) n = f.Read(buffer, 0, buffer.Length);
                string head = System.Text.Encoding.ASCII.GetString(buffer, 0, n);
                int at = head.IndexOf("shaders=", StringComparison.Ordinal);
                if (at < 0) return null;
                int end = head.IndexOf(' ', at);
                return end < 0 ? null : head.Substring(at + 8, end - at - 8);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        private static string Stamp(string exe)
        {
            var g = Global();
            return "for shaders=" + (EngineShaders(exe) ?? "unknown") + " mode=" + (g != null && g.TrueResolutionActive ? "true-res" : "native");
        }
        /// <summary>True when the next launch would find no record of the game's shaders to build behind its screen: none
        /// recorded yet, none for the resolution mode now chosen, or one an engine with another shader translation wrote
        /// (which the engine starts over). Once prepared for an engine's translation and a mode it isn't offered again,
        /// whatever the warm-up managed to record.</summary>
        public static bool Needed(string exe)
        {
            string full = Path.GetFullPath(exe);
            var record = new FileInfo(RecordPath(full));
            string engine = EngineShaders(full);
            bool usable = record.Exists && record.Length >= 4096 &&
                          (engine == null || RecordShaders(record.FullName) == null || RecordShaders(record.FullName) == engine);
            if (usable) return false;
            string marker = Path.Combine(Path.GetDirectoryName(full), MarkerName);
            try { return !File.Exists(marker) || !File.ReadAllText(marker).Contains(Stamp(full)); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        /// <summary>Remembers that the first launch was prepared (or skipped) for this engine and mode, so it is offered once.</summary>
        public static void Mark(string exe, string result)
        {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), MarkerName), "First launch prepared " + DateTime.Now.ToString("u") + " " + Stamp(exe) + Environment.NewLine + result + Environment.NewLine); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Where a game's key art shows its character: the square, 40% of the picture's side, with the most skin in it,
    /// counted twice in its middle (a face, not a sunset's rim). Skin is a warm hue of 5 to 32 degrees at a middle
    /// lightness, which keeps out a logo's gold. Empty when the art shows none, and the whole picture stands in.
    /// The engine's preparing screen finds its runner the same way (ui.c game_runner).
    /// </summary>
    public static class KeyArtSubject
    {
        private static bool Skin(Color c)
        {
            int r = c.R, g = c.G, b = c.B, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            float h = c.GetHue(), l = c.GetBrightness();
            return r > 95 && g > 40 && b > 20 && mx - mn > 15 && Math.Abs(r - g) > 15 && r > g && r > b && h >= 5 && h <= 32 && l >= 0.3f && l <= 0.86f;
        }
        public static Rectangle Find(Bitmap art)
        {
            const int n = 64;
            using (var small = new Bitmap(art, n, n))
            {
                var skin = new int[n + 1, n + 1]; // summed area table
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        skin[x + 1, y + 1] = (Skin(small.GetPixel(x, y)) ? 1 : 0) + skin[x, y + 1] + skin[x + 1, y] - skin[x, y];
                Func<int, int, int, int, int> sum = (x0, y0, x1, y1) => skin[x1, y1] - skin[x0, y1] - skin[x1, y0] + skin[x0, y0];
                int side = n * 40 / 100, best = -1, bx = 0, by = 0, q = side / 4;
                for (int y0 = 0; y0 + side <= n; y0++)
                    for (int x0 = 0; x0 + side <= n; x0++)
                    {
                        int score = sum(x0, y0, x0 + side, y0 + side) + 2 * sum(x0 + q, y0 + q, x0 + side - q, y0 + side - q);
                        if (score > best) { best = score; bx = x0; by = y0; }
                    }
                if (best < side * side / 20) return Rectangle.Empty; /* no face to speak of */
                float k = (float)art.Width / n;
                return new Rectangle((int)(bx * k), (int)(by * k), (int)(side * k), (int)(side * k));
            }
        }
    }

    /// <summary>
    /// The game's own loading screen while its first launch is prepared, full screen as a game's own: its key art
    /// blurred behind, its poster, its name, and along the foot a bar the game's character runs along as it fills,
    /// bobbing, leaning into its stride and kicking up dust. It can't be skipped: the game starts once its shaders are
    /// built, so its first play doesn't stutter on them.
    /// </summary>
    public sealed class PreparingScreen : Form
    {
        private static readonly Color Accent = Color.FromArgb(230, 59, 75), Ink = Color.FromArgb(236, 239, 244), Soft = Color.FromArgb(175, 184, 200);
        private sealed class Puff { public float X, Y, Age, Size; }
        private readonly Bitmap blurred, poster, runner;
        private Bitmap backdrop;
        private readonly string title;
        private readonly double expectedSeconds;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer tick = new Timer { Interval = 16 };
        private readonly List<Puff> puffs = new List<Puff>();
        private double lastPuff, lastFrame;
        private bool finished;
        private string status = "Starting the game hidden";

        public string Status { get { return status; } set { status = value ?? ""; } }
        /// <summary>Set by its owner once the warm-up is over: until then the screen doesn't close (Alt+F4 included).</summary>
        public bool Done { get; set; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Done && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        public PreparingScreen(string exe, double expectedSeconds)
        {
            string full = Path.GetFullPath(exe), name = Path.GetFileNameWithoutExtension(full);
            title = name.ToUpperInvariant();
            this.expectedSeconds = expectedSeconds > 1 ? expectedSeconds : 60;
            string art = Path.Combine(Path.GetDirectoryName(full), name + "_data", "icon.jpg");
            try
            {
                if (File.Exists(art))
                    using (var stream = new MemoryStream(File.ReadAllBytes(art)))
                    using (var image = Image.FromStream(stream))
                    {
                        poster = new Bitmap(image);
                        blurred = Shrink(poster, 24); // drawn stretched back up, a soft wash of its colours
                        var subject = KeyArtSubject.Find(poster);
                        runner = subject.IsEmpty ? new Bitmap(poster) : poster.Clone(subject, poster.PixelFormat);
                    }
            }
            catch (ArgumentException) { poster = blurred = runner = null; }
            catch (IOException) { poster = blurred = runner = null; }
            Text = "Preparing " + name;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = Color.FromArgb(14, 16, 22);
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = true;
            AppIcon.Apply(this);
            tick.Tick += (s, e) => Invalidate();
            tick.Start();
        }
        /// <summary>The play-through finished: the bar fills before the screen closes.</summary>
        public void Finish() { finished = true; status = "Ready"; Invalidate(); Update(); }
        private static Bitmap Shrink(Image image, int side)
        {
            var small = new Bitmap(side, side);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, new Rectangle(0, 0, side, side));
            }
            return small;
        }
        /// <summary>The key art blurred over w x h, darker toward the foot, made once a size: bilinear scaling, as
        /// bicubic stretching a 24-texel picture to a screen overshoots into dark specks</summary>
        private Bitmap Backdrop(int w, int h)
        {
            var b = new Bitmap(Math.Max(1, w), Math.Max(1, h));
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(BackColor);
                if (blurred != null)
                {
                    /* up in steps of four, each one smooth: one stretch of 24 texels to a screen left dark specks */
                    Bitmap step = blurred;
                    for (int side = 96; side <= 384; side *= 4)
                    {
                        var next = new Bitmap(side, side);
                        using (var sg = Graphics.FromImage(next))
                        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                        {
                            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            sg.DrawImage(step, new Rectangle(0, 0, side, side), 0, 0, step.Width, step.Height, GraphicsUnit.Pixel, attributes);
                        }
                        if (step != blurred) step.Dispose();
                        step = next;
                    }
                    float cover = Math.Max((float)w / step.Width, (float)h / step.Height), sw = w / cover, sh = h / cover;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(step, new Rectangle(0, 0, w, h), (step.Width - sw) / 2, (step.Height - sh) / 2, sw, sh, GraphicsUnit.Pixel, attributes);
                    }
                    step.Dispose();
                }
                using (var shade = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), Color.FromArgb(120, 0, 0, 0), Color.FromArgb(235, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(shade, 0, 0, w, h);
            }
            return b;
        }
        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            double t = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, t - lastFrame);
            lastFrame = t;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (backdrop == null || backdrop.Width != w || backdrop.Height != h)
            {
                if (backdrop != null) backdrop.Dispose();
                backdrop = Backdrop(w, h);
            }
            g.DrawImageUnscaled(backdrop, 0, 0);

            // the poster and the name, a little above the middle
            float side = poster != null ? h * 0.40f : 0, gap = poster != null ? h * 0.05f : 0, textWidth = Math.Min(w * 0.42f, h * 0.9f);
            float x0 = (w - side - gap - textWidth) / 2, y0 = h * 0.40f - side / 2;
            if (poster != null)
            {
                var card = new RectangleF(x0, y0, side, side);
                using (var glow = new SolidBrush(Color.FromArgb(70, Accent))) using (var p = Rounded(RectangleF.Inflate(card, h * 0.012f, h * 0.012f), h * 0.02f)) g.FillPath(glow, p);
                using (var frame = new SolidBrush(Accent)) using (var p = Rounded(RectangleF.Inflate(card, h * 0.005f, h * 0.005f), h * 0.016f)) g.FillPath(frame, p);
                using (var p = Rounded(card, h * 0.012f)) { g.SetClip(p); g.DrawImage(poster, card); g.ResetClip(); }
            }
            float tx = x0 + side + gap, ty = y0 + side * 0.18f;
            using (var label = new Font("Segoe UI", h * 0.016f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var big = new Font("Segoe UI", h * 0.05f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Segoe UI", h * 0.019f, GraphicsUnit.Pixel))
            {
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(g, "PREPARING THE FIRST LAUNCH", label, new Point((int)tx, (int)ty), Accent, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, title, big, new Rectangle((int)tx, (int)(ty + h * 0.03f), (int)textWidth, (int)(h * 0.14f)), Ink, flags);
                TextRenderer.DrawText(g, "Building the game's shaders by playing its opening once, hidden, so it runs smoothly from the start. This happens only on the first launch, and your save is backed up and put back.", body, new Rectangle((int)tx, (int)(ty + h * 0.17f), (int)textWidth, (int)(h * 0.12f)), Soft, flags);

                // the bar along the foot, and the character running at its front
                double fraction = finished ? 1 : Math.Min(0.97, t / expectedSeconds);
                float bx = w * 0.08f, bw = w * 0.84f, by = h * 0.86f, bh = Math.Max(6, h * 0.009f);
                using (var track = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) using (var p = Rounded(new RectangleF(bx, by, bw, bh), bh / 2)) g.FillPath(track, p);
                float fill = (float)(bw * fraction);
                if (fill > bh) using (var accent = new SolidBrush(Accent)) using (var p = Rounded(new RectangleF(bx, by, fill, bh), bh / 2)) g.FillPath(accent, p);
                if (runner != null) DrawRunner(g, bx + fill, by, h, t, dt);
                TextRenderer.DrawText(g, (int)(fraction * 100) + "%   " + status, body, new Point((int)bx, (int)(by + bh + h * 0.02f)), Ink, TextFormatFlags.NoPadding);
                const string onceText = "Only on the first launch";
                var size = TextRenderer.MeasureText(g, onceText, body, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, onceText, body, new Point((int)(bx + bw) - size.Width, (int)(by + bh + h * 0.02f)), Soft, TextFormatFlags.NoPadding);
            }
        }
        /// <summary>The character's badge running at x along a bar whose top is at barY: a stride every 0.4 s, up and
        /// down with it, squashed as it lands and leaning into the run, dust puffing behind each footfall.</summary>
        private void DrawRunner(Graphics g, float x, float barY, int h, double t, double dt)
        {
            float d = h * 0.11f, phase = (float)(t / 0.4 % 1.0);
            float hop = (float)Math.Abs(Math.Sin(phase * Math.PI)) * d * 0.32f;
            float squash = hop < d * 0.04f ? 0.9f : 1f;
            float lean = 7f + (float)Math.Sin(t / 0.4 * 2 * Math.PI) * 4f;
            float footY = barY - h * 0.004f;
            if (t - lastPuff > 0.2 && hop < d * 0.06f)
            {
                lastPuff = t;
                puffs.Add(new Puff { X = x - d * 0.2f, Y = footY, Size = d * 0.16f });
            }
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i];
                p.Age += (float)dt;
                p.X -= (float)dt * d * 0.9f;
                p.Y -= (float)dt * d * 0.25f;
                if (p.Age > 0.6f) { puffs.RemoveAt(i); continue; }
                float s = p.Size * (1 + p.Age * 2.5f);
                using (var b = new SolidBrush(Color.FromArgb((int)(150 * (1 - p.Age / 0.6f)), 230, 225, 215)))
                    g.FillEllipse(b, p.X - s / 2, p.Y - s / 2, s, s);
            }
            // its shadow on the bar, smaller as it rises
            float shadow = d * (0.7f - hop / d);
            using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillEllipse(b, x - shadow / 2, barY - shadow * 0.12f, shadow, shadow * 0.24f);
            var state = g.Save();
            g.TranslateTransform(x, footY - hop - d * squash / 2);
            g.RotateTransform(lean);
            g.ScaleTransform(1f / squash * 0.98f, squash);
            var circle = new RectangleF(-d / 2, -d / 2, d, d);
            using (var ring = new SolidBrush(Accent)) g.FillEllipse(ring, RectangleF.Inflate(circle, d * 0.06f, d * 0.06f));
            using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, RectangleF.Inflate(circle, d * 0.025f, d * 0.025f));
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(circle);
                g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(runner, circle);
            }
            g.Restore(state);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Dispose(); if (poster != null) poster.Dispose(); if (blurred != null) blurred.Dispose(); if (runner != null) runner.Dispose(); if (backdrop != null) backdrop.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

using System;
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
        /// <summary>The engine's record of the game's shaders: shader_cache\&lt;game&gt;.vkshaders beside the exe.</summary>
        public static string RecordPath(string exe)
        {
            string full = Path.GetFullPath(exe);
            return Path.Combine(Path.GetDirectoryName(full), "shader_cache", Path.GetFileNameWithoutExtension(full) + ".vkshaders");
        }
        /// <summary>True for a game PortForge hasn't prepared yet and whose engine has recorded none of its shaders.</summary>
        public static bool Needed(string exe)
        {
            string full = Path.GetFullPath(exe);
            if (File.Exists(Path.Combine(Path.GetDirectoryName(full), MarkerName))) return false;
            var record = new FileInfo(RecordPath(full));
            return !record.Exists || record.Length < 4096;
        }
        /// <summary>Remembers that the first launch was prepared (or skipped), so it is offered once.</summary>
        public static void Mark(string exe, string result)
        {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), MarkerName), "First launch prepared " + DateTime.Now.ToString("u") + Environment.NewLine + result + Environment.NewLine); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The game's own screen while its first launch is prepared, in the engine's colours: its key art blurred behind,
    /// its poster in a red frame, its name, and a bar of how far the hidden play-through has got. Esc or Skip starts the
    /// game at once.
    /// </summary>
    public sealed class PreparingScreen : Form
    {
        private static readonly Color Accent = Color.FromArgb(230, 59, 75), Ink = Color.FromArgb(236, 239, 244), Soft = Color.FromArgb(160, 170, 188);
        private readonly Bitmap blurred, poster;
        private readonly string title;
        private readonly double expectedSeconds;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer tick = new Timer { Interval = 50 };
        private Rectangle skipArea;
        private bool finished;
        private string status = "Starting the game hidden";

        /// <summary>The player asked to start the game at once.</summary>
        public event EventHandler SkipRequested;
        public string Status { get { return status; } set { status = value ?? ""; Invalidate(); } }

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
                        blurred = Shrink(image, 24); // drawn stretched back up, a soft wash of its colours
                    }
            }
            catch (ArgumentException) { poster = blurred = null; }
            catch (IOException) { poster = blurred = null; }
            Text = "Preparing " + name;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1180, area.Width * 9 / 10), Math.Min(640, area.Height * 9 / 10));
            BackColor = Color.FromArgb(20, 24, 34);
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = true;
            AppIcon.Apply(this);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Skip(); };
            MouseUp += (s, e) => { if (skipArea.Contains(e.Location)) Skip(); };
            MouseMove += (s, e) => Cursor = skipArea.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            tick.Tick += (s, e) => Invalidate();
            tick.Start();
        }
        /// <summary>The play-through finished: the bar fills before the screen closes.</summary>
        public void Finish() { finished = true; status = "Ready"; Invalidate(); Update(); }
        private void Skip()
        {
            status = "Starting the game";
            var handler = SkipRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }
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
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (blurred != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                // cover the window, cut to its shape from the middle, a texel in from each edge
                float cover = Math.Max((float)w / blurred.Width, (float)h / blurred.Height);
                float sw = w / cover, sh = h / cover;
                using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(blurred, new Rectangle(0, 0, w, h), (blurred.Width - sw) / 2, (blurred.Height - sh) / 2, sw, sh, GraphicsUnit.Pixel, attributes);
                }
            }
            using (var shade = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(blurred != null ? 140 : 0, 0, 0, 0), Color.FromArgb(blurred != null ? 225 : 90, 0, 0, 0), LinearGradientMode.Vertical))
                g.FillRectangle(shade, 0, 0, w, h);
            int side = poster != null ? h * 46 / 100 : 0, gap = poster != null ? 44 : 0;
            int textWidth = Math.Min(560, w - side - gap - 120);
            int x0 = (w - side - gap - textWidth) / 2, y0 = (h - Math.Max(side, 300)) / 2;
            if (poster != null)
            {
                using (var frame = new SolidBrush(Accent)) g.FillRectangle(frame, x0 - 4, y0 - 4, side + 8, side + 8);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(poster, new Rectangle(x0, y0, side, side));
            }
            int tx = x0 + side + gap, ty = y0 + Math.Max(0, (side - 300) / 2);
            using (var small = new Font("Segoe UI", 11, FontStyle.Bold))
            using (var big = new Font("Segoe UI", 26, FontStyle.Bold))
            using (var body = new Font("Segoe UI", 11))
            using (var accent = new SolidBrush(Accent))
            {
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(g, "PREPARING THE FIRST LAUNCH", small, new Point(tx, ty), Accent, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, title, big, new Rectangle(tx, ty + 24, textWidth, 100), Ink, flags);
                TextRenderer.DrawText(g, "Playing the game's opening once, hidden, so its shaders are built before you play. This happens only on the first launch.", body, new Rectangle(tx, ty + 130, textWidth, 50), Soft, flags);
                double fraction = finished ? 1 : Math.Min(0.97, clock.Elapsed.TotalSeconds / expectedSeconds);
                int barY = ty + 196, barH = 8;
                using (var track = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) g.FillRectangle(track, tx, barY, textWidth, barH);
                g.FillRectangle(accent, tx, barY, (int)(textWidth * fraction), barH);
                TextRenderer.DrawText(g, (int)(fraction * 100) + "%   " + status, body, new Rectangle(tx, barY + 18, textWidth, 24), Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "Your save is backed up and put back afterwards.", body, new Point(tx, barY + 52), Soft, TextFormatFlags.NoPadding);
                const string skipText = "Esc: skip and start now";
                var size = TextRenderer.MeasureText(g, skipText, body, Size.Empty, TextFormatFlags.NoPadding);
                skipArea = new Rectangle(tx, barY + 80, size.Width, size.Height);
                TextRenderer.DrawText(g, skipText, body, skipArea.Location, Soft, TextFormatFlags.NoPadding);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Dispose(); if (poster != null) poster.Dispose(); if (blurred != null) blurred.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

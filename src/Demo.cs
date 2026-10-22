using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PortForgeDemo
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            if (args.Length != 1 || !File.Exists(args[0]))
            {
                MessageBox.Show("Launch this demo from the exported game app, with its scene.txt file.", "PortForge packaging demo");
                return;
            }
            Application.Run(new OrbitWindow(File.ReadAllText(args[0]).Trim()));
        }
    }

    internal sealed class OrbitWindow : Form
    {
        private readonly Timer timer = new Timer { Interval = 16 };
        private double time;
        private float x = 450, y = 300;
        private bool left, right, up, down;
        private readonly string scene;
        public OrbitWindow(string name)
        {
            scene = name;
            Text = "Orbit Playground | PortForge packaging demo";
            ClientSize = new Size(900, 600);
            BackColor = Color.FromArgb(12, 16, 24);
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            KeyDown += delegate(object sender, KeyEventArgs e) { KeysChanged(e.KeyCode, true); if (e.KeyCode == Keys.Escape) Close(); };
            KeyUp += delegate(object sender, KeyEventArgs e) { KeysChanged(e.KeyCode, false); };
            Deactivate += delegate { left = right = up = down = false; };
            timer.Tick += delegate
            {
                time += .016;
                x += (right ? 4 : 0) - (left ? 4 : 0);
                y += (down ? 4 : 0) - (up ? 4 : 0);
                x = Math.Max(20, Math.Min(ClientSize.Width - 20, x));
                y = Math.Max(120, Math.Min(ClientSize.Height - 50, y));
                Invalidate();
            };
            timer.Start();
        }
        private void KeysChanged(Keys key, bool value)
        {
            if (key == Keys.A || key == Keys.Left) left = value;
            if (key == Keys.D || key == Keys.Right) right = value;
            if (key == Keys.W || key == Keys.Up) up = value;
            if (key == Keys.S || key == Keys.Down) down = value;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var heading = new Font("Segoe UI", 24, FontStyle.Bold)) g.DrawString("Orbit Playground", heading, Brushes.White, 30, 26);
            using (var small = new Font("Segoe UI", 10))
            using (var muted = new SolidBrush(Color.FromArgb(160, 173, 193)))
            {
                g.DrawString("WASD / arrows to move   |   Esc to exit", small, muted, 33, 77);
                g.DrawString("Packaging demo only. No console emulation.   Scene: " + scene, small, muted, 30, ClientSize.Height - 32);
            }
            float cx = ClientSize.Width / 2f, cy = ClientSize.Height / 2f + 40;
            using (var line = new Pen(Color.FromArgb(38, 49, 66)))
            using (var planet = new SolidBrush(Color.FromArgb(117, 141, 245)))
            using (var ship = new SolidBrush(Color.FromArgb(105, 226, 183)))
            {
                for (int i = 1; i <= 4; i++)
                {
                    float radius = 43 + i * 39;
                    g.DrawEllipse(line, cx - radius, cy - radius, radius * 2, radius * 2);
                    float px = cx + (float)Math.Cos(time * (1.2 / i) + i) * radius;
                    float py = cy + (float)Math.Sin(time * (1.2 / i) + i) * radius;
                    g.FillEllipse(planet, px - 6, py - 6, 12, 12);
                }
                g.FillEllipse(ship, cx - 13, cy - 13, 26, 26);
                g.FillPolygon(ship, new[] { new PointF(x, y - 13), new PointF(x - 10, y + 10), new PointF(x, y + 5), new PointF(x + 10, y + 10) });
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}

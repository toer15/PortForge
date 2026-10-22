using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// Draws the PortForge logo and writes assets\portforge.ico (16 to 256 px) and assets\logo.png.
internal static class MakeIcon
{
    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            float s = size / 512f; g.ScaleTransform(s, s);
            // the tile
            using (var tile = Rounded(new RectangleF(16, 16, 480, 480), 112))
            {
                using (var fill = new LinearGradientBrush(new PointF(0, 16), new PointF(0, 496), Color.FromArgb(27, 36, 54), Color.FromArgb(10, 14, 22))) g.FillPath(fill, tile);
                using (var rim = new Pen(Color.FromArgb(105, 226, 183), 10)) g.DrawPath(rim, tile);
            }
            // the forge glow under the letter
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(86, 330, 340, 130);
                using (var pg = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(150, 105, 226, 183), SurroundColors = new[] { Color.FromArgb(0, 105, 226, 183) } }) g.FillPath(pg, glow);
            }
            // a bold P
            using (var path = new GraphicsPath())
            using (var family = FontFamily.GenericSansSerif)
            {
                string face = "Segoe UI Black";
                FontFamily chosen = family;
                try { chosen = new FontFamily(face); } catch (Exception) { }
                path.AddString("P", chosen, (int)FontStyle.Bold, 390, new PointF(104, 36), StringFormat.GenericTypographic);
                var b = path.GetBounds();
                using (var m = new Matrix()) { m.Translate(256 - (b.X + b.Width / 2), 262 - (b.Y + b.Height / 2)); path.Transform(m); }
                using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) { using (var sp = (GraphicsPath)path.Clone()) { using (var m = new Matrix()) { m.Translate(0, 10); sp.Transform(m); } g.FillPath(shadow, sp); } }
                var pb = path.GetBounds();
                using (var letter = new LinearGradientBrush(new PointF(0, pb.Top), new PointF(0, pb.Bottom), Color.FromArgb(158, 247, 214), Color.FromArgb(62, 196, 156))) g.FillPath(letter, path);
            }
            // a spark leaving the anvil: a four-point star
            var spark = new PointF[] { new PointF(392, 92), new PointF(404, 128), new PointF(440, 140), new PointF(404, 152), new PointF(392, 188), new PointF(380, 152), new PointF(344, 140), new PointF(380, 128) };
            using (var star = new SolidBrush(Color.White)) g.FillPolygon(star, spark);
            using (var small = new SolidBrush(Color.FromArgb(200, 158, 247, 214))) g.FillEllipse(small, 316, 80, 20, 20);
        }
        return bmp;
    }

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int size in sizes)
            using (var bmp = Draw(size)) using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
        using (var bmp = Draw(512)) bmp.Save(Path.Combine(outDir, "logo.png"), ImageFormat.Png);
        using (var file = new FileStream(Path.Combine(outDir, "portforge.ico"), FileMode.Create))
        using (var w = new BinaryWriter(file))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset); offset += images[i].Length;
            }
            foreach (var data in images) w.Write(data);
        }
        Console.WriteLine("wrote " + Path.Combine(outDir, "portforge.ico"));
        return 0;
    }
}

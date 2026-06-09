using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Usage: MakeIcon <source.png> <out.ico> <out256.png>
string srcPath = args[0];
string outIco = args[1];
string outPng = args[2];

using var src = new Bitmap(srcPath);

// 1) Detect the badge bounding box: foreground = anything that is not near-white
//    (the white page background and the faint glow halo are excluded).
int minX = src.Width, minY = src.Height, maxX = 0, maxY = 0;
for (int y = 0; y < src.Height; y++)
{
    for (int x = 0; x < src.Width; x++)
    {
        Color c = src.GetPixel(x, y);
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        bool foreground = c.A > 10 && min < 215;
        if (foreground)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }
}

Console.WriteLine($"bbox: x[{minX}..{maxX}] y[{minY}..{maxY}]  ({maxX - minX + 1}x{maxY - minY + 1}) of {src.Width}x{src.Height}");

// 2) Square the box around its center.
int w = maxX - minX + 1, h = maxY - minY + 1;
int size = Math.Max(w, h);
int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
int sx = Math.Max(0, cx - size / 2), sy = Math.Max(0, cy - size / 2);
if (sx + size > src.Width) sx = src.Width - size;
if (sy + size > src.Height) sy = src.Height - size;

using var square = new Bitmap(size, size, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(square))
{
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle(sx, sy, size, size), GraphicsUnit.Pixel);
}

// 3) Render a 256px master with rounded corners (transparent outside the rounded rect).
Bitmap master = RenderRounded(square, 256, 0.20f);
master.Save(outPng, ImageFormat.Png);
Console.WriteLine($"wrote {outPng}");

// 4) Write a multi-resolution .ico (PNG-compressed entries, Vista+).
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var pngs = new List<byte[]>();
foreach (int s in sizes)
{
    using var bmp = RenderRounded(square, s, 0.20f);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    pngs.Add(ms.ToArray());
}

using (var fs = new FileStream(outIco, FileMode.Create))
using (var bw = new BinaryWriter(fs))
{
    bw.Write((ushort)0);            // reserved
    bw.Write((ushort)1);            // type = icon
    bw.Write((ushort)sizes.Length); // image count

    int offset = 6 + sizes.Length * 16;
    for (int i = 0; i < sizes.Length; i++)
    {
        int s = sizes[i];
        bw.Write((byte)(s >= 256 ? 0 : s)); // width  (0 = 256)
        bw.Write((byte)(s >= 256 ? 0 : s)); // height
        bw.Write((byte)0);                  // palette
        bw.Write((byte)0);                  // reserved
        bw.Write((ushort)1);                // planes
        bw.Write((ushort)32);               // bpp
        bw.Write((uint)pngs[i].Length);     // size in bytes
        bw.Write((uint)offset);             // offset
        offset += pngs[i].Length;
    }

    foreach (byte[] png in pngs)
    {
        bw.Write(png);
    }
}

Console.WriteLine($"wrote {outIco} with sizes: {string.Join(",", sizes)}");

static Bitmap RenderRounded(Bitmap source, int size, float radiusFraction)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    float r = size * radiusFraction;
    using var path = new GraphicsPath();
    path.AddArc(0, 0, 2 * r, 2 * r, 180, 90);
    path.AddArc(size - 2 * r, 0, 2 * r, 2 * r, 270, 90);
    path.AddArc(size - 2 * r, size - 2 * r, 2 * r, 2 * r, 0, 90);
    path.AddArc(0, size - 2 * r, 2 * r, 2 * r, 90, 90);
    path.CloseFigure();

    g.SetClip(path);
    g.DrawImage(source, new Rectangle(0, 0, size, size));
    return bmp;
}

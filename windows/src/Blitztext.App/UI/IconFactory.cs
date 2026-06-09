using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Blitztext.App.UI;

/// <summary>
/// Produces the tray icon from the embedded brand badge, overlaying a coloured status dot
/// (red/amber/green) for non-idle states — the Windows counterpart to the macOS menubar
/// status. Also exposes the brand image as a WPF <see cref="System.Windows.Media.ImageSource"/>
/// for window icons.
/// </summary>
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Bitmap BaseImage = LoadBaseImage();

    public static readonly System.Windows.Media.ImageSource WindowIcon = LoadWindowIcon();

    private static Bitmap LoadBaseImage()
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("blitztext-icon.png");
        if (s == null)
        {
            // Fallback: a plain blue square so the app still has a tray icon.
            var fallback = new Bitmap(256, 256);
            using var g = Graphics.FromImage(fallback);
            g.Clear(Color.FromArgb(0x2B, 0x3A, 0x8F));
            return fallback;
        }

        return new Bitmap(s);
    }

    private static System.Windows.Media.ImageSource LoadWindowIcon()
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("blitztext-icon.png");
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = s ?? new MemoryStream();
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Brand icon, with a status dot overlaid for non-idle states.</summary>
    public static Icon CreateStatusIcon(AppStatusKind kind)
    {
        const int size = 64;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            g.DrawImage(BaseImage, new Rectangle(0, 0, size, size));

            if (kind != AppStatusKind.Idle)
            {
                float d = size * 0.42f;
                float x = size - d - 2;
                float y = size - d - 2;

                using var ring = new SolidBrush(Color.White);
                g.FillEllipse(ring, x - 2, y - 2, d + 4, d + 4);
                using var dot = new SolidBrush(AccentFor(kind));
                g.FillEllipse(dot, x, y, d, d);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Color AccentFor(AppStatusKind kind) => kind switch
    {
        AppStatusKind.Recording => Color.FromArgb(0xE0, 0x4B, 0x4B),  // red
        AppStatusKind.Processing => Color.FromArgb(0xF5, 0x9E, 0x0B), // amber
        AppStatusKind.Success => Color.FromArgb(0x22, 0xAA, 0x55),    // green
        AppStatusKind.Error => Color.FromArgb(0xCC, 0x33, 0x33),      // dark red
        _ => Color.FromArgb(0x3B, 0x82, 0xF6),                        // blue (unused for idle)
    };
}

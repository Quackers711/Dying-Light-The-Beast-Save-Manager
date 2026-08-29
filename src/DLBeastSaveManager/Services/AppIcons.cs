using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DLBeastSaveManager.Services;

public static class AppIcons
{
    public static readonly Color Watching = Color.FromArgb(0x3F, 0xB9, 0x50);
    public static readonly Color Idle = Color.FromArgb(0x8A, 0x8F, 0x98);
    public static readonly Color Attention = Color.FromArgb(0xE8, 0xA3, 0x17);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Bitmap Draw(Color accent, int size = 32)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var s = size / 32f;

        using var shield = new GraphicsPath();
        shield.AddLines(new[]
        {
            new PointF(16 * s, 2 * s),
            new PointF(28 * s, 7 * s),
            new PointF(28 * s, 17 * s),
            new PointF(16 * s, 30 * s),
            new PointF(4 * s, 17 * s),
            new PointF(4 * s, 7 * s)
        });
        shield.CloseFigure();

        using var body = new SolidBrush(Color.FromArgb(0x23, 0x26, 0x2B));
        g.FillPath(body, shield);

        using var edge = new Pen(accent, 2.4f * s) { LineJoin = LineJoin.Round };
        g.DrawPath(edge, shield);

        using var dot = new SolidBrush(accent);
        g.FillEllipse(dot, 11.5f * s, 11 * s, 9 * s, 9 * s);

        return bitmap;
    }

    public static Icon CreateIcon(Color accent, int size = 32)
    {
        using var bitmap = Draw(accent, size);
        var handle = bitmap.GetHicon();
        try
        {
            using var shared = Icon.FromHandle(handle);
            return (Icon)shared.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static BitmapImage CreateWindowIcon(Color accent, int size = 64)
    {
        using var bitmap = Draw(accent, size);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ChildLock.Native;

namespace ChildLock;

/// <summary>
/// Draws the padlock icons at runtime so they are crisp at any DPI. A colored disc keeps them
/// visible on both light and dark taskbars.
/// </summary>
internal static class IconFactory
{
    // Segoe MDL2 Assets ships with Windows 10 and 11.
    const string GlyphFont = "Segoe MDL2 Assets";
    const string LockGlyph = "";
    const string UnlockGlyph = "";

    public static readonly Color LockedColor = Color.FromArgb(196, 43, 28);
    public static readonly Color UnlockedColor = Color.FromArgb(0, 103, 192);
    public static readonly Color PausedColor = Color.FromArgb(110, 110, 110);

    public static Bitmap Draw(int size, bool locked, Color? background = null)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using (var disc = new SolidBrush(background ?? (locked ? LockedColor : UnlockedColor)))
            g.FillEllipse(disc, 0, 0, size - 1, size - 1);

        using var font = new Font(GlyphFont, size * 0.48f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(locked ? LockGlyph : UnlockGlyph, font, Brushes.White, new RectangleF(0, size * 0.03f, size, size), format);
        return bmp;
    }

    /// <summary>Makes an Icon that owns its own copy of the handle.</summary>
    public static Icon ToIcon(Bitmap bmp)
    {
        nint handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    /// <summary>The padlock as pixels for PureTech.Tray, which scales it to the size the shell wants.</summary>
    public static PureTech.Tray.Rgba TrayPixels(bool locked, bool paused, int size = 64)
    {
        using var bmp = Draw(size, locked, paused && !locked ? PausedColor : null);
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bgra = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * size * 4, size * 4);
            return PureTech.Tray.Rgba.FromBgra(bgra, size, size);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// The icon toasts and the Start menu shortcut show, written once to local app data. Null if it can't be written.
    /// </summary>
    public static string? AppIconPath()
    {
        string path = Path.Combine(LocalDataDirectory, "childlock.ico");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(LocalDataDirectory);
                WriteIco(path);
            }
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log("couldn't write app icon: " + e.Message);
            return null;
        }
    }

    public static string LocalDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChildLock");

    public static Icon TrayIcon(bool locked, bool paused)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = Draw(size, locked, paused && !locked ? PausedColor : null);
        return ToIcon(bmp);
    }

    /// <summary>Writes a multi-size .ico (PNG entries): the exe's icon at dev time, and the toast icon.</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(s =>
        {
            using var bmp = Draw(s, locked: true, background: UnlockedColor);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) w.Write(image);
    }
}

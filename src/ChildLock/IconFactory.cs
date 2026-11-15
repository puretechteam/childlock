using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ChildLock.Native;
using ChildLock.Ui;

namespace ChildLock;

/// <summary>
/// Draws the padlock icons at runtime so they are crisp at any DPI: a white padlock on a rounded
/// colored tile, which stays visible on both light and dark taskbars. The shackle is open when unlocked.
/// </summary>
internal static class IconFactory
{
    /// <summary>The cached toast and Start menu icon. The name changes with the artwork, so an old one is never kept.</summary>
    const string IconFileName = "childlock-2.ico";
    const string OldIconFileName = "childlock.ico";

    // Below this size the padlock is drawn with heavier lines and no keyhole, so it still reads.
    const int BoldBelow = 32;

    public static readonly Color LockedColor = Color.FromArgb(196, 43, 28);
    public static readonly Color UnlockedColor = Color.FromArgb(0, 103, 192);
    public static readonly Color PausedColor = Color.FromArgb(110, 110, 110);

    /// <param name="bold">Heavier lines and no keyhole; by default for sizes under 32 px.</param>
    public static Bitmap Draw(int size, bool locked, Color? background = null, bool? bold = null)
    {
        bool heavy = bold ?? size < BoldBelow;
        if (size < BoldBelow)
        {
            // Tiny sizes: draw big and average down, which keeps edges cleaner than drawing 16 px shapes directly.
            using var big = Draw(size * 4, locked, background, heavy);
            var small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var sg = Graphics.FromImage(small);
            sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sg.PixelOffsetMode = PixelOffsetMode.Half;
            sg.DrawImage(big, new Rectangle(0, 0, size, size));
            return small;
        }

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(Color.Transparent);

        var color = background ?? (locked ? LockedColor : UnlockedColor);
        var tile = new RectangleF(0, 0, size, size);
        using (var shape = Theme.RoundedRect(tile, size * 0.22f))
        using (var fill = new LinearGradientBrush(new PointF(0, -1), new PointF(0, size + 1), Theme.Shade(color, 0.2f), Theme.Shade(color, -0.12f)))
            g.FillPath(fill, shape);

        DrawPadlock(g, tile, Color.White, open: !locked, heavy, keyhole: heavy ? null : Theme.Shade(color, -0.12f));
        return bmp;
    }

    /// <summary>A padlock centered in <paramref name="box"/> (a square), sized to it. Also used on the badge.</summary>
    public static void DrawPadlock(Graphics g, RectangleF box, Color color, bool open, bool bold, Color? keyhole = null)
    {
        float u = box.Width;
        float cx = box.X + u / 2;
        float stroke = u * (bold ? 0.105f : 0.085f);
        float bodyWidth = u * (bold ? 0.50f : 0.46f);
        float bodyHeight = u * (bold ? 0.35f : 0.33f);
        float bodyTop = box.Y + u * 0.46f;
        float radius = u * (bold ? 0.15f : 0.135f);          // of the shackle's center line
        float arcY = box.Y + u * (open ? 0.30f : 0.365f);    // an open shackle is lifted

        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Flat, EndCap = open ? LineCap.Round : LineCap.Flat };
        using (var shackle = new GraphicsPath())
        {
            shackle.AddLine(cx - radius, bodyTop + stroke / 2, cx - radius, arcY);
            shackle.AddArc(cx - radius, arcY - radius, radius * 2, radius * 2, 180, 180);
            // Closed: the right leg goes into the body. Open: it stops in the air above it.
            shackle.AddLine(cx + radius, arcY, cx + radius, open ? arcY + u * 0.05f : bodyTop + stroke / 2);
            g.DrawPath(pen, shackle);
        }
        using (var body = Theme.RoundedRect(new RectangleF(cx - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight), u * 0.07f))
            g.FillPath(brush, body);

        if (keyhole is { } hole)
        {
            using var dark = new SolidBrush(hole);
            float holeY = bodyTop + bodyHeight * 0.40f;
            float r = u * 0.045f;
            g.FillEllipse(dark, cx - r, holeY - r, r * 2, r * 2);
            g.FillRectangle(dark, cx - u * 0.02f, holeY, u * 0.04f, u * 0.09f);
        }
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
        // Bold, because the shell shows this at 16 to 32 px.
        using var bmp = Draw(size, locked, paused && !locked ? PausedColor : null, bold: true);
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
    /// The icon toasts and the Start menu shortcut show, written once. Null if it can't be written.
    /// </summary>
    /// <param name="folder">Where to keep it; local app data by default. A copy run with its own settings file
    /// keeps it next to that file, so it never touches the real one.</param>
    public static string? AppIconPath(string? folder = null)
    {
        folder ??= LocalDataDirectory;
        string path = Path.Combine(folder, IconFileName);
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                WriteIco(path);
                File.Delete(Path.Combine(folder, OldIconFileName));
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

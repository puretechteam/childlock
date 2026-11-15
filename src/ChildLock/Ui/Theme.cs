using System.Drawing.Drawing2D;

namespace ChildLock.Ui;

/// <summary>Colors and shapes for ChildLock's own windows. Light or dark follows the app's color mode.</summary>
internal static class Theme
{
    public static bool Dark => Application.IsDarkModeEnabled;

    public static Color Window => Dark ? Rgb(32, 32, 32) : Rgb(243, 243, 243);
    public static Color Card => Dark ? Rgb(43, 43, 43) : Rgb(251, 251, 251);
    public static Color CardBorder => Dark ? Rgb(29, 29, 29) : Rgb(229, 229, 229);
    public static Color Divider => Dark ? Rgb(56, 56, 56) : Rgb(234, 234, 234);
    public static Color Text => Dark ? Rgb(255, 255, 255) : Rgb(27, 27, 27);
    public static Color DimText => Dark ? Rgb(190, 190, 190) : Rgb(96, 96, 96);
    public static Color Accent => Dark ? Rgb(96, 205, 255) : Rgb(0, 103, 192);
    public static Color OnAccent => Dark ? Rgb(0, 0, 0) : Rgb(255, 255, 255);
    public static Color Warning => Dark ? Rgb(252, 185, 65) : Rgb(157, 93, 0);
    public static Color Error => Dark ? Rgb(255, 130, 118) : Rgb(196, 43, 28);
    public static Color Button => Dark ? Rgb(55, 55, 55) : Rgb(251, 251, 251);
    public static Color ButtonBorder => Dark ? Rgb(72, 72, 72) : Rgb(204, 204, 204);
    public static Color SwitchOff => Dark ? Rgb(200, 200, 200) : Rgb(92, 92, 92);

    public static Font Body { get; } = new("Segoe UI", 10f);
    public static Font Small { get; } = new("Segoe UI", 9f);
    public static Font Heading { get; } = new("Segoe UI Semibold", 10f);
    public static Font Title { get; } = new("Segoe UI Semibold", 15f);

    /// <summary>Moves a color toward white (amount above 0) or black (below 0); 1 or -1 goes all the way.</summary>
    public static Color Shade(Color color, float amount)
    {
        int target = amount > 0 ? 255 : 0;
        float t = Math.Abs(amount);
        return Color.FromArgb(color.A, Mix(color.R), Mix(color.G), Mix(color.B));
        int Mix(int channel) => (int)Math.Round(channel + (target - channel) * t);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);
}

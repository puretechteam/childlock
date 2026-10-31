using System.Drawing.Drawing2D;
using System.Drawing.Text;
using static ChildLock.Native.NativeMethods;

namespace ChildLock;

/// <summary>What one badge shows.</summary>
/// <param name="Dots">Filled dots under the title, one per character of the word typed so far.</param>
/// <param name="Progress">0 to 1 for a bar along the bottom, 0 for none.</param>
internal sealed record BadgeContent(string Title, string Subtitle, BadgeStyle Style, int Dots = 0, double Progress = 0);

internal enum BadgeStyle
{
    Countdown,
    Locked,
    Holding,
}

/// <summary>
/// Small pill in the top-right corner of one screen. It never takes focus and clicks pass
/// straight through it, so whatever is playing keeps playing.
/// </summary>
internal sealed class BadgeForm : Form
{
    const string LockGlyph = "";
    const string UnlockGlyph = "";
    const string TimerGlyph = "";
    const int MaxDots = 12;

    readonly Screen _screen;
    BadgeContent _content = new("", "", BadgeStyle.Locked);

    public BadgeForm(Screen screen)
    {
        _screen = screen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 32);
        Opacity = 0.88;
        DoubleBuffered = true;
        Text = "ChildLock";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    public void ShowContent(BadgeContent content)
    {
        bool resize = !Visible || content.Title != _content.Title || content.Subtitle != _content.Subtitle
            || (content.Dots > 0) != (_content.Dots > 0) || Math.Min(content.Dots, MaxDots) != Math.Min(_content.Dots, MaxDots);
        _content = content;
        if (resize) LayoutBadge();
        if (!Visible) Show();
        EnsureTopmost();
        Invalidate();
    }

    /// <summary>Puts the badge back on top in case a full-screen player covered it.</summary>
    public void EnsureTopmost()
    {
        if (IsHandleCreated && Visible)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    float DpiScale => DeviceDpi / 96f;

    static Font TitleFont() => new("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
    static Font SubtitleFont() => new("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

    void LayoutBadge()
    {
        float s = DpiScale;
        using var title = TitleFont();
        using var sub = SubtitleFont();
        using var g = CreateGraphics();
        var titleSize = g.MeasureString(_content.Title, title);
        var subSize = SubtitleSize(g, sub, s);

        int pad = (int)(12 * s);
        int glyph = (int)(22 * s);
        int textWidth = (int)Math.Ceiling(Math.Max(titleSize.Width, subSize.Width));
        int height = (int)Math.Ceiling(titleSize.Height + subSize.Height) + pad;
        height = Math.Max(height, glyph + pad);
        int width = pad + glyph + (int)(8 * s) + textWidth + pad + (int)(4 * s);

        var area = _screen.WorkingArea;
        int margin = (int)(16 * s);
        Bounds = new Rectangle(area.Right - width - margin, area.Top + margin, width, height);

        using var path = RoundedRect(new Rectangle(0, 0, width, height), (int)(10 * s));
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    SizeF SubtitleSize(Graphics g, Font sub, float s)
    {
        if (_content.Dots > 0)
        {
            int dots = Math.Min(_content.Dots, MaxDots);
            return new SizeF(dots * 10 * s, 12 * s);
        }
        return _content.Subtitle.Length == 0 ? SizeF.Empty : g.MeasureString(_content.Subtitle, sub);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DpiScale;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int pad = (int)(12 * s);
        int glyphSize = (int)(22 * s);
        using var glyphFont = new Font("Segoe MDL2 Assets", 14f, FontStyle.Regular, GraphicsUnit.Point);
        using var title = TitleFont();
        using var sub = SubtitleFont();
        var accent = _content.Style switch
        {
            BadgeStyle.Countdown => Color.FromArgb(255, 185, 0),
            BadgeStyle.Holding => Color.FromArgb(108, 203, 95),
            _ => Color.FromArgb(255, 99, 82),
        };
        string glyph = _content.Style switch
        {
            BadgeStyle.Countdown => TimerGlyph,
            BadgeStyle.Holding => UnlockGlyph,
            _ => LockGlyph,
        };

        using (var brush = new SolidBrush(accent))
        using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString(glyph, glyphFont, brush, new RectangleF(pad, 0, glyphSize, Height), center);
        }

        float x = pad + glyphSize + 8 * s;
        var titleSize = g.MeasureString(_content.Title, title);
        var subSize = SubtitleSize(g, sub, s);
        float y = (Height - titleSize.Height - subSize.Height) / 2;
        g.DrawString(_content.Title, title, Brushes.White, x, y);

        if (_content.Dots > 0)
        {
            using var dot = new SolidBrush(Color.FromArgb(220, 220, 220));
            float d = 6 * s;
            float dotY = y + titleSize.Height + (subSize.Height - d) / 2;
            for (int i = 0; i < Math.Min(_content.Dots, MaxDots); i++)
                g.FillEllipse(dot, x + 1 * s + i * 10 * s, dotY, d, d);
        }
        else if (_content.Subtitle.Length > 0)
        {
            using var dim = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString(_content.Subtitle, sub, dim, x, y + titleSize.Height);
        }

        if (_content.Progress > 0)
        {
            using var bar = new SolidBrush(accent);
            float h = Math.Max(2, 3 * s);
            g.FillRectangle(bar, 0, Height - h, (float)(Width * Math.Min(1, _content.Progress)), h);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) LayoutBadge();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Region?.Dispose();
        base.Dispose(disposing);
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ChildLock.Ui;
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
/// straight through it, so whatever is playing keeps playing. It is a picture with its own
/// transparency (smooth corners, soft shadow) that fades in and out.
/// </summary>
internal sealed class BadgeForm : Form
{
    const string TimerGlyph = ""; // Segoe MDL2 Assets, which ships with Windows 10 and 11
    const int MaxDots = 12;
    const int FadeInMs = 150;
    const int FadeOutMs = 200;

    readonly Screen _screen;
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    BadgeContent? _content;
    Bitmap? _image;
    double _alpha;
    double _target;
    long _fadeTick;

    public BadgeForm(Screen screen)
    {
        _screen = screen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "ChildLock";
        _fade.Tick += (_, _) => FadeStep();
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
        if (content != _content || _image is null)
        {
            _content = content;
            Redraw();
        }
        if (!Visible)
        {
            _alpha = 0;
            Show();
        }
        FadeTo(1);
        EnsureTopmost();
    }

    /// <summary>Fades the badge away, then hides it.</summary>
    public void FadeOut()
    {
        if (Visible) FadeTo(0);
    }

    /// <summary>Puts the badge back on top in case a full-screen player covered it.</summary>
    public void EnsureTopmost()
    {
        if (IsHandleCreated && Visible)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    void Redraw()
    {
        float s = DeviceDpi / 96f;
        _image?.Dispose();
        _image = Render(_content!, s);
        int shadow = ShadowSize(s);
        int margin = (int)(16 * s);
        var area = _screen.WorkingArea;
        // The picture includes the shadow around the pill; the pill itself sits 16 px from the corner.
        Bounds = new Rectangle(area.Right - margin - _image.Width + shadow, area.Top + margin - shadow, _image.Width, _image.Height);
        Push();
    }

    void FadeTo(double target)
    {
        _target = target;
        if (Math.Abs(_alpha - target) < 0.001)
        {
            Push();
            return;
        }
        _fadeTick = Environment.TickCount64;
        _fade.Start();
    }

    void FadeStep()
    {
        long now = Environment.TickCount64;
        double step = (now - _fadeTick) / (double)(_target > _alpha ? FadeInMs : FadeOutMs);
        _fadeTick = now;
        _alpha = _target > _alpha ? Math.Min(_target, _alpha + step) : Math.Max(_target, _alpha - step);
        Push();
        if (Math.Abs(_alpha - _target) > 0.001) return;
        _fade.Stop();
        if (_target == 0) Hide();
    }

    /// <summary>Hands the picture to Windows with the current fade level.</summary>
    void Push()
    {
        if (_image is null || !IsHandleCreated || !Visible) return;
        nint screenDc = GetDC(0);
        nint memoryDc = CreateCompatibleDC(screenDc);
        nint bitmap = _image.GetHbitmap(Color.FromArgb(0));
        nint previous = SelectObject(memoryDc, bitmap);
        try
        {
            var size = new SIZE { cx = _image.Width, cy = _image.Height };
            var from = new POINT();
            var to = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)Math.Round(_alpha * 255), AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screenDc, ref to, ref size, memoryDc, ref from, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            _ = ReleaseDC(0, screenDc);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Push();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible && _content is not null) Redraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fade.Dispose();
            _image?.Dispose();
        }
        base.Dispose(disposing);
    }

    static int ShadowSize(float s) => (int)Math.Ceiling(10 * s);

    /// <summary>The badge as a picture with transparency: shadow, pill, icon and text.</summary>
    /// <param name="s">Screen scale: 1 at 100%, 1.5 at 150%.</param>
    public static Bitmap Render(BadgeContent content, float s)
    {
        using var title = new Font("Segoe UI Semibold", 14.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sub = new Font("Segoe UI", 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        int dots = Math.Min(content.Dots, MaxDots);

        SizeF titleSize, subSize;
        using (var scratch = new Bitmap(1, 1))
        using (var measure = Graphics.FromImage(scratch))
        {
            measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            titleSize = measure.MeasureString(content.Title, title);
            subSize = dots > 0 ? new SizeF(dots * 10 * s, 12 * s)
                : content.Subtitle.Length == 0 ? SizeF.Empty : measure.MeasureString(content.Subtitle, sub);
        }

        float pad = 12 * s;
        float icon = 22 * s;
        int width = (int)Math.Ceiling(pad + icon + 8 * s + Math.Max(titleSize.Width, subSize.Width) + pad);
        int height = (int)Math.Ceiling(Math.Max(titleSize.Height + subSize.Height + pad, icon + pad));
        int shadow = ShadowSize(s);

        var bmp = new Bitmap(width + shadow * 2, height + shadow * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var pill = new RectangleF(shadow, shadow, width, height);
        float radius = 10 * s;

        // Soft shadow: faint layers that add up toward the pill, shifted down a little.
        using (var layer = new SolidBrush(Color.FromArgb(3, 0, 0, 0)))
        {
            for (int i = shadow; i >= 1; i--)
            {
                var spread = RectangleF.Inflate(pill, i, i);
                spread.Offset(0, 2 * s);
                using var path = Theme.RoundedRect(spread, radius + i);
                g.FillPath(layer, path);
            }
        }

        var accent = content.Style switch
        {
            BadgeStyle.Countdown => Color.FromArgb(255, 185, 0),
            BadgeStyle.Holding => Color.FromArgb(108, 203, 95),
            _ => Color.FromArgb(255, 99, 82),
        };

        using (var shape = Theme.RoundedRect(pill, radius))
        {
            using var fill = new SolidBrush(Color.FromArgb(232, 30, 30, 30));
            g.FillPath(fill, shape);

            if (content.Progress > 0)
            {
                var state = g.Save();
                g.SetClip(shape);
                using var bar = new SolidBrush(accent);
                float h = Math.Max(2, 3 * s);
                g.FillRectangle(bar, pill.X, pill.Bottom - h, (float)(pill.Width * Math.Min(1, content.Progress)), h);
                g.Restore(state);
            }
        }
        // A thin light edge keeps the badge readable on dark video.
        using (var edge = new Pen(Color.FromArgb(56, 255, 255, 255), Math.Max(1, s)))
        using (var inner = Theme.RoundedRect(RectangleF.Inflate(pill, -edge.Width / 2, -edge.Width / 2), radius - edge.Width / 2))
            g.DrawPath(edge, inner);

        var iconBox = new RectangleF(pill.X + pad, pill.Y + (pill.Height - icon) / 2, icon, icon);
        if (content.Style == BadgeStyle.Countdown)
        {
            using var glyphFont = new Font("Segoe MDL2 Assets", 18.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(accent);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(TimerGlyph, glyphFont, brush, iconBox, center);
        }
        else
        {
            // The padlock is drawn in a slightly larger box so it matches the weight of the text.
            IconFactory.DrawPadlock(g, RectangleF.Inflate(iconBox, 3 * s, 3 * s), accent, open: content.Style == BadgeStyle.Holding, bold: false);
        }

        float x = iconBox.Right + 8 * s;
        float y = pill.Y + (pill.Height - titleSize.Height - subSize.Height) / 2;
        g.DrawString(content.Title, title, Brushes.White, x, y);
        if (dots > 0)
        {
            using var dot = new SolidBrush(Color.FromArgb(220, 220, 220));
            float d = 6 * s;
            float dotY = y + titleSize.Height + (subSize.Height - d) / 2;
            for (int i = 0; i < dots; i++)
                g.FillEllipse(dot, x + 2 * s + i * 10 * s, dotY, d, d);
        }
        else if (content.Subtitle.Length > 0)
        {
            using var dim = new SolidBrush(Color.FromArgb(205, 205, 205));
            g.DrawString(content.Subtitle, sub, dim, x, y + titleSize.Height);
        }
        return bmp;
    }
}

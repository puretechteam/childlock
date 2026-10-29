using System.Drawing.Drawing2D;
using System.Drawing.Text;
using static ChildLock.Native.NativeMethods;

namespace ChildLock;

/// <summary>
/// Small "Locked" pill in the top-right corner of the main screen. It never takes focus and
/// clicks pass straight through it, so whatever is playing keeps playing.
/// </summary>
internal sealed class BadgeForm : Form
{
    const string LockGlyph = "";
    const string TimerGlyph = "";

    string _title = "";
    string _subtitle = "";
    bool _counting;

    public BadgeForm()
    {
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

    public void ShowCountdown(int seconds) =>
        ShowText($"Locking in {seconds}...", "Press Esc to cancel", counting: true);

    public void ShowLocked() => ShowText("Locked", "", counting: false);

    void ShowText(string title, string subtitle, bool counting)
    {
        _title = title;
        _subtitle = subtitle;
        _counting = counting;
        LayoutBadge();
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

    void LayoutBadge()
    {
        float s = DpiScale;
        using var title = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        using var sub = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var g = CreateGraphics();
        var titleSize = g.MeasureString(_title, title);
        var subSize = _subtitle.Length == 0 ? SizeF.Empty : g.MeasureString(_subtitle, sub);

        int pad = (int)(12 * s);
        int glyph = (int)(22 * s);
        int textWidth = (int)Math.Ceiling(Math.Max(titleSize.Width, subSize.Width));
        int height = (int)Math.Ceiling(titleSize.Height + subSize.Height) + pad;
        height = Math.Max(height, glyph + pad);
        int width = pad + glyph + (int)(8 * s) + textWidth + pad + (int)(4 * s);

        var area = Screen.PrimaryScreen!.WorkingArea;
        int margin = (int)(16 * s);
        Bounds = new Rectangle(area.Right - width - margin, area.Top + margin, width, height);

        using var path = RoundedRect(new Rectangle(0, 0, width, height), (int)(10 * s));
        Region = new Region(path);
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
        using var title = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        using var sub = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        var accent = _counting ? Color.FromArgb(255, 185, 0) : Color.FromArgb(255, 99, 82);

        using (var brush = new SolidBrush(accent))
        using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString(_counting ? TimerGlyph : LockGlyph, glyphFont, brush,
                new RectangleF(pad, 0, glyphSize, Height), center);
        }

        float x = pad + glyphSize + 8 * s;
        var titleSize = g.MeasureString(_title, title);
        var subSize = _subtitle.Length == 0 ? SizeF.Empty : g.MeasureString(_subtitle, sub);
        float y = (Height - titleSize.Height - subSize.Height) / 2;
        g.DrawString(_title, title, Brushes.White, x, y);
        if (_subtitle.Length > 0)
        {
            using var dim = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString(_subtitle, sub, dim, x, y + titleSize.Height);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) LayoutBadge();
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

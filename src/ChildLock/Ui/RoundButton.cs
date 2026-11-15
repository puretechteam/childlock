using System.Drawing.Drawing2D;

namespace ChildLock.Ui;

/// <summary>A rounded button: filled with the accent color when it is the main action, quiet otherwise.</summary>
internal sealed class RoundButton : Button
{
    readonly bool _primary;
    bool _hover;
    bool _down;

    public RoundButton(string text, bool primary = false)
    {
        _primary = primary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        // Flat keeps the drawing ours; in dark mode Windows draws standard buttons itself.
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Text = text;
        Font = Theme.Body;
        AutoSize = false;
        int width = Math.Max(LogicalToDeviceUnits(96), TextRenderer.MeasureText(text, Font).Width + LogicalToDeviceUnits(32));
        Size = new Size(width, LogicalToDeviceUnits(32));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = _down = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _down = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _down = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Toward the window color when hovered or pressed, which reads right in both light and dark.
        float press = _down ? 0.2f : _hover ? 0.1f : 0;
        var fill = _primary ? Theme.Accent : Theme.Button;
        fill = Blend(fill, Theme.Window, press);
        var text = !Enabled ? Theme.DimText : _primary ? Theme.OnAccent : Theme.Text;

        using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), LogicalToDeviceUnits(5));
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (!_primary)
        {
            using var edge = new Pen(Theme.ButtonBorder);
            g.DrawPath(edge, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(_primary ? Theme.OnAccent : Theme.Text, 1f) { DashStyle = DashStyle.Dot };
            using var inner = Theme.RoundedRect(new RectangleF(3.5f, 3.5f, Width - 7, Height - 7), LogicalToDeviceUnits(3));
            g.DrawPath(ring, inner);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, fill,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

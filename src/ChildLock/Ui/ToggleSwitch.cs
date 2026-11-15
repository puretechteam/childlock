using System.Drawing.Drawing2D;

namespace ChildLock.Ui;

/// <summary>
/// A check box drawn as an on/off switch. Only the painting differs, so the keyboard (Space), clicks,
/// <see cref="CheckBox.Checked"/> and screen readers all work as for any check box.
/// </summary>
internal sealed class ToggleSwitch : CheckBox
{
    bool _hover;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(LogicalToDeviceUnits(40), LogicalToDeviceUnits(20));
        Cursor = Cursors.Hand;
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
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
        _hover = false;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var track = new RectangleF(1, 1, Width - 2, Height - 2);
        float knob = track.Height - (Checked ? 6 : 8) * DeviceDpi / 96f + (_hover ? 1 : 0);
        float inset = (track.Height - knob) / 2;
        using var path = Theme.RoundedRect(track, track.Height / 2);
        if (Checked)
        {
            using var fill = new SolidBrush(Enabled ? Theme.Shade(Theme.Accent, _hover ? (Theme.Dark ? -0.08f : 0.1f) : 0) : Theme.Divider);
            using var dot = new SolidBrush(Theme.OnAccent);
            g.FillPath(fill, path);
            g.FillEllipse(dot, track.Right - inset - knob, track.Y + inset, knob, knob);
        }
        else
        {
            using var edge = new Pen(Enabled ? Theme.SwitchOff : Theme.Divider, 1f);
            using var dot = new SolidBrush(Enabled ? Theme.SwitchOff : Theme.Divider);
            g.DrawPath(edge, path);
            g.FillEllipse(dot, track.X + inset, track.Y + inset, knob, knob);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Theme.Text, 1f) { DashStyle = DashStyle.Dot };
            using var outline = Theme.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f);
            g.DrawPath(ring, outline);
        }
    }
}

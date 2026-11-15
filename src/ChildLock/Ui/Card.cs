using System.Drawing.Drawing2D;

namespace ChildLock.Ui;

/// <summary>
/// A rounded panel of setting rows: a title and a short description on the left, the control on the right,
/// and a thin line between rows.
/// </summary>
internal sealed class Card : TableLayoutPanel
{
    readonly int _width;

    public Card(int width)
    {
        _width = width;
        ColumnCount = 1;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(width, 0);
        MaximumSize = new Size(width, 0);
        BackColor = Theme.Card;
        // Room at the top and bottom keeps the square rows clear of the rounded corners.
        Padding = new Padding(1, LogicalToDeviceUnits(3), 1, LogicalToDeviceUnits(3));
        Margin = Padding.Empty;
        DoubleBuffered = true;
    }

    /// <returns>The stack holding the row's text, so a caller can add a line under the description.</returns>
    public FlowLayoutPanel AddRow(string title, string description, Control control)
    {
        int edge = LogicalToDeviceUnits(16);
        control.Anchor = AnchorStyles.Right;
        control.Margin = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(8), edge, LogicalToDeviceUnits(8));
        int textWidth = _width - 2 - edge - control.Width - control.Margin.Horizontal;

        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(edge, LogicalToDeviceUnits(9), 0, LogicalToDeviceUnits(10)),
        };
        text.Controls.Add(new Label
        {
            Text = title, AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text,
            MaximumSize = new Size(textWidth, 0), Margin = Padding.Empty,
        });
        text.Controls.Add(Note(description, Theme.DimText, textWidth));

        // Each row is its own two-column strip, so a narrow control (a switch) leaves its text more room
        // than a wide one (a text box) does.
        var strip = new TableLayoutPanel
        {
            ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(_width - 2, 0), MaximumSize = new Size(_width - 2, 0),
            Margin = new Padding(0, RowCount > 0 ? 1 : 0, 0, 0), // a 1 px gap for the line between rows
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.Controls.Add(text, 0, 0);
        strip.Controls.Add(control, 1, 0);
        Controls.Add(strip, 0, RowCount++);
        return text;
    }

    /// <summary>A small line of text sized for a row's text column.</summary>
    public static Label Note(string text, Color color, int width) => new()
    {
        Text = text, AutoSize = true, Font = Theme.Small, ForeColor = color,
        MaximumSize = new Size(width, 0), Margin = new Padding(0, 2, 0, 0),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        using (var line = new Pen(Theme.Divider))
        {
            for (int i = 1; i < Controls.Count; i++)
                g.DrawLine(line, 1, Controls[i].Top - 1, Width - 2, Controls[i].Top - 1);
        }

        // Round the corners: paint what lies outside the outline in the window color, then draw the outline.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), LogicalToDeviceUnits(8));
        using var outside = new Region(ClientRectangle);
        outside.Exclude(outline);
        using var window = new SolidBrush(Parent?.BackColor ?? Theme.Window);
        using var border = new Pen(Theme.CardBorder);
        g.FillRegion(window, outside);
        g.DrawPath(border, outline);
    }
}

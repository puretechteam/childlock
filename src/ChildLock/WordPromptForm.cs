using ChildLock.Core;

namespace ChildLock;

/// <summary>Asks for the unlock word before Settings, pausing or exiting, when that protection is on.</summary>
internal sealed class WordPromptForm : Form
{
    static readonly Font HeadingFont = new((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 12f, FontStyle.Bold);

    readonly string _word;
    readonly TextBox _input = new() { UseSystemPasswordChar = true, MaxLength = UnlockWord.MaxLength + 16 };
    // Always laid out (empty until a wrong try) so the window doesn't jump when it appears.
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(220, 60, 50) };
    readonly PictureBox _icon = new() { SizeMode = PictureBoxSizeMode.AutoSize };

    public WordPromptForm(string word, string action)
    {
        _word = UnlockWord.Normalize(word);
        Text = "ChildLock";
        Icon = IconFactory.TrayIcon(locked: true, paused: false);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(LogicalToDeviceUnits(20));
        TopMost = true;

        int textWidth = LogicalToDeviceUnits(300);
        _icon.Image = IconFactory.Draw(LogicalToDeviceUnits(40), locked: true);
        _icon.Margin = new Padding(0, 2, LogicalToDeviceUnits(16), 0);
        _input.Width = textWidth;
        _input.Margin = new Padding(0, LogicalToDeviceUnits(14), 0, 0);
        _error.Margin = new Padding(0, LogicalToDeviceUnits(6), 0, 0);
        _error.MaximumSize = new Size(textWidth, 0);

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(84), 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = ok.MinimumSize, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Check();
        AcceptButton = ok;
        CancelButton = cancel;
        _input.TextChanged += (_, _) => _error.Text = "";

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
        };
        cancel.Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, 0);
        ok.Margin = Padding.Empty;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var heading = new Label { Text = "Enter the unlock word", AutoSize = true, Font = HeadingFont, Margin = Padding.Empty };
        var body = new Label
        {
            Text = $"Type the unlock word to {action}.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0),
        };

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(_icon, 0, 0);
        grid.SetRowSpan(_icon, 4);
        grid.Controls.Add(heading, 1, 0);
        grid.Controls.Add(body, 1, 1);
        grid.Controls.Add(_input, 1, 2);
        grid.Controls.Add(_error, 1, 3);
        grid.Controls.Add(buttons, 0, 4);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _input.Focus();
    }

    void Check()
    {
        if (UnlockWord.Normalize(_input.Text) == _word)
        {
            DialogResult = DialogResult.OK;
            return;
        }
        _input.SelectAll();
        _input.Focus();
        _error.Text = "That's not the unlock word. Try again.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon?.Dispose();
            _icon.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}

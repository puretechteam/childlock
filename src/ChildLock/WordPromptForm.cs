using ChildLock.Core;

namespace ChildLock;

/// <summary>Asks for the unlock word before Settings, pausing or exiting, when that protection is on.</summary>
internal sealed class WordPromptForm : Form
{
    readonly string _word;
    readonly TextBox _input = new() { Width = 260, UseSystemPasswordChar = true, MaxLength = UnlockWord.MaxLength + 16 };
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(220, 60, 50) };

    public WordPromptForm(string word, string action)
    {
        _word = UnlockWord.Normalize(word);
        Text = "ChildLock";
        Icon = IconFactory.TrayIcon(locked: true, paused: false);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        TopMost = true;

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Check();
        AcceptButton = ok;
        CancelButton = cancel;
        _input.TextChanged += (_, _) => _error.Text = "";

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        stack.Controls.Add(new Label { Text = $"Type the unlock word to {action}.", AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        stack.Controls.Add(_input);
        stack.Controls.Add(_error);
        stack.Controls.Add(buttons);
        Controls.Add(stack);
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
        _error.Text = "That's not the unlock word.";
        _input.SelectAll();
        _input.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}

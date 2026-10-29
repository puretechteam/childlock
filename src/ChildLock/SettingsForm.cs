using ChildLock.Core;

namespace ChildLock;

/// <summary>What the settings window hands back when you press Save.</summary>
internal sealed record SettingsChoice(Settings Settings, bool StartAtLogin);

internal sealed class SettingsForm : Form
{
    readonly Func<SettingsChoice, string?> _apply;
    readonly Settings _original;
    readonly HotkeyBox _hotkey = new() { Width = 220 };
    readonly TextBox _word = new() { Width = 220, MaxLength = UnlockWord.MaxLength };
    readonly NumericUpDown _countdown = new() { Minimum = 0, Maximum = Settings.MaxCountdownSeconds, Width = 64 };
    readonly NumericUpDown _idle = new() { Minimum = 0, Maximum = Settings.MaxIdleLockMinutes, Width = 64 };
    readonly CheckBox _sound = new() { Text = "Play a sound when locking and unlocking", AutoSize = true };
    readonly CheckBox _startAtLogin = new() { Text = "Start ChildLock when I sign in", AutoSize = true };
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(220, 60, 50), MaximumSize = new Size(440, 0) };

    /// <param name="apply">Validates and applies the choice. Returns null on success or an error to show.</param>
    public SettingsForm(Settings settings, bool startAtLogin, Func<SettingsChoice, string?> apply)
    {
        _original = settings;
        _apply = apply;

        Text = "ChildLock Settings";
        Icon = IconFactory.TrayIcon(locked: true, paused: false);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        ShowInTaskbar = true;

        _hotkey.Value = settings.ParsedHotkey;
        _word.Text = settings.UnlockWord;
        _countdown.Value = settings.CountdownSeconds;
        _idle.Value = settings.IdleLockMinutes;
        _sound.Checked = settings.SoundEnabled;
        _startAtLogin.Checked = startAtLogin;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, "Lock hotkey", _hotkey, "Click the box, then press the keys you want.");
        AddRow(grid, "Unlock word", _word, "Type this while locked to unlock. 4 to 32 letters or digits.");
        AddRow(grid, "Countdown", WithSuffix(_countdown, "seconds before locking (0 = instant)"), null);
        AddRow(grid, "Auto-lock", WithSuffix(_idle, "minutes without input (0 = off)"), null);

        var checks = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        checks.Controls.Add(_sound);
        checks.Controls.Add(_startAtLogin);
        grid.Controls.Add(checks, 1, grid.RowCount++);
        grid.Controls.Add(_error, 1, grid.RowCount++);

        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Save();
        AcceptButton = save;
        CancelButton = cancel;

        var version = new Label
        {
            Text = $"ChildLock {Application.ProductVersion} by Pure Tech",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 24, 0),
        };
        var footer = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        footer.Controls.Add(version);
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        grid.Controls.Add(footer, 0, grid.RowCount++);
        grid.SetColumnSpan(footer, 2);

        Controls.Add(grid);
    }

    static void AddRow(TableLayoutPanel grid, string label, Control field, string? hint)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 16, 0) }, 0, row);
        if (hint is null)
        {
            field.Margin = new Padding(0, 6, 0, 0);
            grid.Controls.Add(field, 1, row);
            return;
        }
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        stack.Controls.Add(field);
        stack.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText });
        grid.Controls.Add(stack, 1, row);
    }

    static Control WithSuffix(Control field, string suffix)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        field.Margin = new Padding(0, 0, 6, 0);
        row.Controls.Add(field);
        row.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(0, 4, 0, 0) });
        return row;
    }

    void Save()
    {
        if (_hotkey.Value.Validate() is { } hotkeyError)
        {
            ShowError(hotkeyError);
            _hotkey.Focus();
            return;
        }
        if (UnlockWord.Validate(_word.Text) is { } wordError)
        {
            ShowError(wordError);
            _word.Focus();
            return;
        }

        var settings = _original with
        {
            LockHotkey = _hotkey.Value.ToString(),
            UnlockWord = UnlockWord.Normalize(_word.Text),
            CountdownSeconds = (int)_countdown.Value,
            IdleLockMinutes = (int)_idle.Value,
            SoundEnabled = _sound.Checked,
        };

        Cursor = Cursors.WaitCursor;
        string? error = _apply(new SettingsChoice(settings, _startAtLogin.Checked));
        Cursor = Cursors.Default;
        if (error is not null)
        {
            ShowError(error);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    void ShowError(string message) => _error.Text = message;
}

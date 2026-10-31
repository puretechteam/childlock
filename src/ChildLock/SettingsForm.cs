using ChildLock.Core;

namespace ChildLock;

/// <summary>What the settings window hands back when you press Save.</summary>
internal sealed record SettingsChoice(Settings Settings, bool StartAtLogin);

internal sealed class SettingsForm : Form
{
    static readonly (LockMode Mode, string Text)[] Modes =
    [
        (LockMode.Full, "Keyboard and mouse"),
        (LockMode.KeyboardOnly, "Keyboard only"),
        (LockMode.MouseOnly, "Mouse only"),
    ];

    static readonly Font HeadingFont = new(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);

    readonly Func<SettingsChoice, string?> _apply;
    readonly Settings _original;
    readonly HotkeyBox _hotkey = new() { Width = 220 };
    readonly ComboBox _mode = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown _countdown = new() { Minimum = 0, Maximum = Settings.MaxCountdownSeconds, Width = 64 };
    readonly NumericUpDown _idle = new() { Minimum = 0, Maximum = Settings.MaxIdleLockMinutes, Width = 64 };
    readonly TextBox _word = new() { Width = 220, MaxLength = UnlockWord.MaxLength };
    readonly Label _wordWarning = new() { AutoSize = true, ForeColor = Color.FromArgb(200, 120, 0), MaximumSize = new Size(380, 0) };
    readonly NumericUpDown _hold = new() { Minimum = 0, Maximum = Settings.MaxHoldSeconds, Width = 64 };
    readonly CheckBox _dots = new() { Text = "Show a dot on the badge for each correct character typed", AutoSize = true };
    readonly CheckBox _tooltipWord = new() { Text = "Show the unlock word when hovering over the tray icon", AutoSize = true };
    readonly CheckBox _protect = new() { Text = "Ask for the unlock word to open Settings, pause or exit", AutoSize = true };
    readonly CheckBox _badge = new() { Text = "Show the \"Locked\" badge (the countdown always shows)", AutoSize = true };
    readonly CheckBox _sound = new() { Text = "Play a sound when locking and unlocking", AutoSize = true };
    readonly NumericUpDown _cleaning = new() { Minimum = 0, Maximum = Settings.MaxCleaningSeconds, Increment = 10, Width = 64 };
    readonly CheckBox _startAtLogin = new() { Text = "Start ChildLock when I sign in", AutoSize = true };
    readonly CheckBox _admin = new() { Text = "Run as administrator, so admin windows such as Task Manager are locked too", AutoSize = true };
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(220, 60, 50), MaximumSize = new Size(480, 0) };

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

        foreach (var (_, text) in Modes) _mode.Items.Add(text);
        _mode.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.Mode == settings.DefaultLockMode));
        _hotkey.Value = settings.ParsedHotkey;
        _countdown.Value = settings.CountdownSeconds;
        _idle.Value = settings.IdleLockMinutes;
        _word.Text = settings.UnlockWord;
        _hold.Value = settings.UnlockHoldSeconds;
        _dots.Checked = settings.ShowTypingProgress;
        _tooltipWord.Checked = settings.ShowWordInTooltip;
        _protect.Checked = settings.RequireWordForSettings;
        _badge.Checked = settings.ShowBadge;
        _sound.Checked = settings.SoundEnabled;
        _cleaning.Value = settings.CleaningAutoEndSeconds;
        _startAtLogin.Checked = startAtLogin;
        _admin.Checked = settings.RunAsAdmin;
        _word.TextChanged += (_, _) => UpdateWordWarning();
        _hotkey.ValueChanged += () => _error.Text = "";
        UpdateWordWarning();

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddHeading(grid, "Locking", first: true);
        AddRow(grid, "Lock hotkey", Stack(_hotkey, Hint("Click the box, then press the keys you want.")));
        AddRow(grid, "Lock", _mode);
        AddRow(grid, "Countdown", WithSuffix(_countdown, "seconds before locking (0 = instant)"));
        AddRow(grid, "Auto-lock", WithSuffix(_idle, "minutes without input (0 = off)"));

        AddHeading(grid, "Unlocking");
        AddRow(grid, "Unlock word", Stack(_word, Hint("Type this while locked. Letters, digits and spaces."), _wordWarning));
        AddRow(grid, "Hold to unlock", WithSuffix(_hold, "seconds holding the lock hotkey (0 = off)"));
        AddRow(grid, "", Stack(_dots, _tooltipWord, _protect));

        AddHeading(grid, "Badge and sound");
        AddRow(grid, "", Stack(_badge, _sound));

        AddHeading(grid, "Cleaning mode");
        AddRow(grid, "Ends after", WithSuffix(_cleaning, "seconds (0 = only when you unlock)"));

        AddHeading(grid, "Startup");
        string adminState = Elevation.IsElevated
            ? "ChildLock is running with admin rights now."
            : "ChildLock is running without admin rights now. Ticking this restarts it and Windows asks once.";
        AddRow(grid, "", Stack(_startAtLogin, _admin, Hint(adminState)));

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

    void UpdateWordWarning()
    {
        _wordWarning.Text = UnlockWord.Validate(_word.Text) is null ? UnlockWord.Weakness(_word.Text) ?? "" : "";
        _wordWarning.Visible = _wordWarning.Text.Length > 0;
    }

    static void AddHeading(TableLayoutPanel grid, string text, bool first = false)
    {
        var heading = new Label
        {
            Text = text,
            AutoSize = true,
            Font = HeadingFont,
            Margin = new Padding(0, first ? 0 : 18, 0, 2),
        };
        int row = grid.RowCount++;
        grid.Controls.Add(heading, 0, row);
        grid.SetColumnSpan(heading, 2);
    }

    static void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        int row = grid.RowCount++;
        if (label.Length > 0)
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(12, 10, 16, 0) }, 0, row);
        field.Margin = new Padding(0, 6, 0, 0);
        grid.Controls.Add(field, 1, row);
    }

    static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(380, 0) };

    static Control Stack(params Control[] controls)
    {
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        foreach (var control in controls) stack.Controls.Add(control);
        return stack;
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
            DefaultLockMode = Modes[Math.Max(0, _mode.SelectedIndex)].Mode,
            CountdownSeconds = (int)_countdown.Value,
            IdleLockMinutes = (int)_idle.Value,
            UnlockWord = UnlockWord.Normalize(_word.Text),
            UnlockHoldSeconds = (int)_hold.Value,
            ShowTypingProgress = _dots.Checked,
            ShowWordInTooltip = _tooltipWord.Checked,
            RequireWordForSettings = _protect.Checked,
            ShowBadge = _badge.Checked,
            SoundEnabled = _sound.Checked,
            CleaningAutoEndSeconds = (int)_cleaning.Value,
            RunAsAdmin = _admin.Checked,
        };

        Cursor = Cursors.WaitCursor;
        string? error = _apply(new SettingsChoice(settings.Normalize(), _startAtLogin.Checked));
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}

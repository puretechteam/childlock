using ChildLock.Core;
using ChildLock.Ui;

namespace ChildLock;

/// <summary>What the settings window hands back when you press Save.</summary>
internal sealed record SettingsChoice(Settings Settings, bool StartAtLogin);

/// <summary>
/// The settings window: a header, one card of rows per group in a scrolling column, and Save and Cancel
/// in a footer that stays put.
/// </summary>
internal sealed class SettingsForm : Form
{
    static readonly (LockMode Mode, string Text)[] Modes =
    [
        (LockMode.Full, "Keyboard and mouse"),
        (LockMode.KeyboardOnly, "Keyboard only"),
        (LockMode.MouseOnly, "Mouse only"),
    ];

    const int CardWidth = 540;
    const int FieldWidth = 190;
    const int MaxHeight = 720;

    readonly Func<SettingsChoice, string?> _apply;
    readonly Settings _original;
    readonly HotkeyBox _hotkey = new();
    readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown _countdown = new() { Minimum = 0, Maximum = Settings.MaxCountdownSeconds };
    readonly NumericUpDown _idle = new() { Minimum = 0, Maximum = Settings.MaxIdleLockMinutes };
    readonly TextBox _word = new() { MaxLength = UnlockWord.MaxLength };
    readonly NumericUpDown _hold = new() { Minimum = 0, Maximum = Settings.MaxHoldSeconds };
    readonly ToggleSwitch _dots = new();
    readonly ToggleSwitch _tooltipWord = new();
    readonly ToggleSwitch _protect = new();
    readonly ToggleSwitch _badge = new();
    readonly ToggleSwitch _sound = new();
    readonly NumericUpDown _cleaning = new() { Minimum = 0, Maximum = Settings.MaxCleaningSeconds, Increment = 10 };
    readonly ToggleSwitch _startAtLogin = new();
    readonly ToggleSwitch _admin = new();
    readonly Label _wordWarning;
    readonly Label _error;
    readonly PictureBox _logo = new() { SizeMode = PictureBoxSizeMode.AutoSize };

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
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = true;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        int cardWidth = S(CardWidth);
        int side = S(24);
        foreach (Control field in new Control[] { _hotkey, _mode, _word }) field.Width = S(FieldWidth);
        foreach (var number in new[] { _countdown, _idle, _hold, _cleaning }) number.Width = S(64);

        _hotkey.BackColor = SystemColors.Window; // read-only boxes default to grey, which reads as disabled

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

        var locking = new Card(cardWidth);
        Row(locking, "Lock hotkey", "Click the box, then press the keys you want.", _hotkey);
        Row(locking, "What to lock", "What the hotkey, Lock now and auto-lock lock.", _mode);
        Row(locking, "Countdown", "Time to step back before the lock starts. 0 locks at once.", _countdown, "seconds");
        Row(locking, "Auto-lock", "Lock after this long without keyboard or mouse input. 0 turns it off.", _idle, "minutes");

        var unlocking = new Card(cardWidth);
        var wordText = Row(unlocking, "Unlock word", "Type this while locked. Letters, digits and spaces.", _word);
        _wordWarning = Card.Note("", Theme.Warning, wordText.Controls[0].MaximumSize.Width);
        wordText.Controls.Add(_wordWarning);
        Row(unlocking, "Hold to unlock", "Hold the lock hotkey this long to unlock without typing. 0 turns it off.", _hold, "seconds");
        Row(unlocking, "Typing dots", "Show a dot on the badge for each correct character typed.", _dots);
        Row(unlocking, "Unlock word in the tray tooltip", "Hover over the tray icon to see the word, in case you forget it.", _tooltipWord);
        Row(unlocking, "Protect Settings", "Ask for the unlock word to open Settings, pause the hotkey or exit.", _protect);

        var badge = new Card(cardWidth);
        Row(badge, "Locked badge", "Show a small badge while locked. The countdown always shows.", _badge);
        Row(badge, "Sound", "Play a short chime when locking and unlocking.", _sound);

        var cleaning = new Card(cardWidth);
        Row(cleaning, "Ends after", "Cleaning mode ends by itself after this long. 0 means only when you unlock.", _cleaning, "seconds");

        var startup = new Card(cardWidth);
        Row(startup, "Start at sign-in", "Start ChildLock when I sign in to Windows.", _startAtLogin);
        Row(startup, "Run as administrator", "So admin windows such as Task Manager are locked too. " + (Elevation.IsElevated
            ? "ChildLock has admin rights now."
            : "ChildLock has no admin rights now; turning this on restarts it and Windows asks once."), _admin);

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(side, 0),
            Padding = new Padding(0, 0, 0, S(8)),
        };
        (string Name, Card Card)[] groups =
            [("Locking", locking), ("Unlocking", unlocking), ("Badge and sound", badge), ("Cleaning mode", cleaning), ("Startup", startup)];
        foreach (var (name, card) in groups)
        {
            content.Controls.Add(new Label
            {
                Text = name, AutoSize = true, Font = Theme.Heading,
                Margin = new Padding(S(2), card == locking ? 0 : S(18), 0, S(6)),
            });
            content.Controls.Add(card);
        }
        var scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        scroll.Controls.Add(content);
        // The wheel scrolls the page; it only changes a number or the list once you have clicked into it.
        foreach (Control field in new Control[] { _mode, _countdown, _idle, _hold, _cleaning })
        {
            field.MouseWheel += (_, e) =>
            {
                if (field.Focused) return;
                ((HandledMouseEventArgs)e).Handled = true;
                scroll.AutoScrollPosition = new Point(0, -scroll.AutoScrollPosition.Y - e.Delta);
            };
        }

        _error = new Label
        {
            AutoSize = true, ForeColor = Theme.Error, Anchor = AnchorStyles.Left,
            MaximumSize = new Size(cardWidth - S(230), 0), Margin = Padding.Empty,
        };
        _word.TextChanged += (_, _) => UpdateWordWarning();
        _hotkey.ValueChanged += () => _error.Text = "";
        UpdateWordWarning();

        var header = Header(side);
        var footer = Footer(side);
        // Docking works from the last control added to the first: the edges claim their space, the scroll area gets the rest.
        Controls.Add(scroll);
        Controls.Add(footer);
        Controls.Add(header);

        // A comfortable height that fits small laptop screens; the cards scroll inside it.
        int wanted = header.PreferredSize.Height + content.PreferredSize.Height + footer.PreferredSize.Height;
        int room = Screen.FromPoint(Cursor.Position).WorkingArea.Height - S(72);
        ClientSize = new Size(cardWidth + side * 2, Math.Min(Math.Min(wanted, room), S(MaxHeight)));
    }

    int S(int logical) => LogicalToDeviceUnits(logical);

    /// <summary>Adds a row; with a <paramref name="unit"/> the control gets that word after it ("3 seconds").</summary>
    FlowLayoutPanel Row(Card card, string title, string description, Control control, string? unit = null)
    {
        control.AccessibleName = title;
        control.Font = Theme.Body;
        if (unit is null) return card.AddRow(title, description, control);

        // Every unit gets the same width, so the number boxes line up from row to row.
        int unitWidth = TextRenderer.MeasureText("seconds", Theme.Body).Width + S(2);
        var label = new Label
        {
            Text = unit, AutoSize = false, Size = new Size(unitWidth, control.PreferredSize.Height), TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.DimText, Margin = Padding.Empty,
        };
        control.Margin = new Padding(0, 0, S(8), 0);
        var pair = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Size = new Size(control.Width + S(8) + unitWidth, control.PreferredSize.Height) };
        pair.Controls.Add(control);
        pair.Controls.Add(label);
        return card.AddRow(title, description, pair);
    }

    Control Header(int side)
    {
        _logo.Image = IconFactory.Draw(S(40), locked: true, IconFactory.UnlockedColor);
        _logo.Margin = new Padding(0, 0, S(14), 0);
        _logo.Anchor = AnchorStyles.Left;

        var names = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        names.Controls.Add(new Label { Text = "ChildLock", AutoSize = true, Font = Theme.Title, Margin = Padding.Empty });
        names.Controls.Add(new Label { Text = "Keyboard and mouse lock", AutoSize = true, Font = Theme.Small, ForeColor = Theme.DimText, Margin = new Padding(S(2), 0, 0, 0) });

        var header = new TableLayoutPanel
        {
            ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            Padding = new Padding(side, S(18), side, S(16)),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(_logo, 0, 0);
        header.Controls.Add(names, 1, 0);
        header.Controls.Add(new Label
        {
            Text = $"Version {Application.ProductVersion}\nby Pure Tech", AutoSize = true, Font = Theme.Small, ForeColor = Theme.DimText,
            TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right, Margin = Padding.Empty,
        }, 2, 0);
        return header;
    }

    Control Footer(int side)
    {
        var save = new RoundButton("Save", primary: true) { DialogResult = DialogResult.None, Margin = new Padding(S(8), 0, 0, 0) };
        var cancel = new RoundButton("Cancel") { DialogResult = DialogResult.Cancel, Margin = new Padding(S(8), 0, 0, 0) };
        save.Click += (_, _) => Save();
        // The window isn't modal, so the button has to close it itself. Esc goes through the same click.
        cancel.Click += (_, _) => Close();
        AcceptButton = save;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var footer = new TableLayoutPanel
        {
            ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Bottom,
            Padding = new Padding(side, S(14), side, S(16)),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_error, 0, 0);
        footer.Controls.Add(buttons, 1, 0);
        return footer;
    }

    void UpdateWordWarning()
    {
        _wordWarning.Text = UnlockWord.Validate(_word.Text) is null ? UnlockWord.Weakness(_word.Text) ?? "" : "";
        _wordWarning.Visible = _wordWarning.Text.Length > 0;
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
        if (disposing)
        {
            Icon?.Dispose();
            _logo.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}

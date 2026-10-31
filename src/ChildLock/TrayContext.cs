using System.ComponentModel;
using ChildLock.Core;
using ChildLock.Native;
using Microsoft.Win32;
using PureTech.Tray;
using Menu = PureTech.Tray.Menu;

namespace ChildLock;

/// <summary>The running app: tray icon, hotkey, hooks, badges and the timers that drive them.</summary>
internal sealed class TrayContext : ApplicationContext
{
    const int RehookEveryTicks = 20; // 20 x 250 ms = 5 s
    const int SecureDesktopMissesToUnlock = 2;

    /// <summary>What the tray menu shows; read on the tray's own thread, so it is replaced, never changed.</summary>
    sealed record MenuState(LockState State, bool Paused, string? Accelerator);

    readonly AppOptions _options;
    readonly SynchronizationContext _ui;
    readonly LockStateMachine _machine = new();
    readonly InputGate _gate;
    readonly InputHooks _hooks;
    readonly HotkeyWindow _hotkeyWindow;
    readonly BadgeSet _badges = new();
    readonly CleaningOverlay _cleaning = new();
    readonly TrayIcon _tray;
    readonly Rgba _readyIcon = IconFactory.TrayPixels(locked: false, paused: false);
    readonly Rgba _lockedIcon = IconFactory.TrayPixels(locked: true, paused: false);
    readonly Rgba _pausedIcon = IconFactory.TrayPixels(locked: false, paused: true);
    readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _watchdogTimer = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 5000 };
    readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 50 };

    Settings _settings;
    SettingsForm? _settingsForm;
    volatile MenuState _menu = new(LockState.Unlocked, false, null);
    bool _hotkeyRegistered;
    bool _idleFired;
    bool _wasLocked;
    bool _prompting;
    bool _exiting;
    int _watchdogTicks;
    int _secureDesktopMisses;

    public TrayContext(AppOptions options, bool elevationDeclined)
    {
        _options = options;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("No UI context");
        _settings = SettingsStore.Load(options.SettingsPath);

        _gate = new InputGate(_machine, _settings.UnlockWord, SystemState.HeldInput);
        ApplyGateSettings();
        _hooks = new InputHooks(_gate);

        // State changes can happen inside a hook callback; defer the UI work so the callback returns fast.
        _machine.StateChanged += (state, reason) => Post(() => OnStateChanged(state, reason));
        _machine.CountdownChanged += seconds => Post(() => _badges.ShowCountdown(seconds));
        _gate.WordProgressChanged += typed => Post(() => OnWordProgress(typed));
        _gate.HoldingChanged += holding => Post(() => OnHoldingChanged(holding));
        _cleaning.TimeUp += _machine.CleaningTimeUp;

        _hotkeyWindow = new HotkeyWindow(options.MutexName);
        _hotkeyWindow.Pressed += () => _machine.HotkeyPressed(_settings.CountdownSeconds, _settings.DefaultLockMode);
        _hotkeyWindow.ShowSettingsRequested += OpenSettings;
        _countdownTimer.Tick += (_, _) => _machine.Tick();
        _watchdogTimer.Tick += (_, _) => Watchdog();
        _idleTimer.Tick += (_, _) => CheckIdle();
        _holdTimer.Tick += (_, _) => PollHold();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        RegisterHotkey();
        _menu = SnapshotMenu();
        _tray = new TrayIcon(_readyIcon, TooltipText(), BuildMenu(), MenuThemeFor(options.Theme));
        ApplyIdleTimer();
        UpdateTray();
        Diag.Log($"started settings={options.SettingsPath} hotkey={_settings.LockHotkey} registered={_hotkeyRegistered} " +
                 $"elevated={Elevation.IsElevated} default_desktop={SystemState.IsDefaultDesktopActive()}");

        (string, Action)[] openSettings = [("Open Settings", () => Post(OpenSettings))];
        if (!_hotkeyRegistered)
            _tray.Notify("Lock hotkey unavailable",
                $"{_settings.LockHotkey} is already used by another app. Pick a different hotkey in Settings.", openSettings);
        else if (!_settings.FirstRunDone)
            _tray.Notify("ChildLock is running",
                $"Press {_settings.LockHotkey} to lock the keyboard and mouse. Type \"{_settings.UnlockWord}\" to unlock.", openSettings);

        if (elevationDeclined)
            _tray.Notify("Running without admin rights",
                "Input aimed at admin windows such as Task Manager isn't blocked. You can turn admin rights back on in Settings.",
                [("Use admin rights", () => Post(RestartElevated)), ("Not now", () => { })]);

        if (!_settings.FirstRunDone)
            SaveSettings(_settings with { FirstRunDone = true });

        if (!options.CustomSettingsPath)
            Task.Run(RepairStartupTask);
    }

    /// <summary>Runs on the UI thread later, unless ChildLock is exiting by then.</summary>
    void Post(Action action) => _ui.Post(_ => { if (!_exiting) action(); }, null);

    // ---- locking ----------------------------------------------------------------------------------------------

    void Lock(LockMode mode) => _machine.TrayLock(_settings.CountdownSeconds, mode);

    void OnStateChanged(LockState state, LockReason reason)
    {
        Diag.Log($"state={state} reason={reason} mode={_machine.Mode}");
        // A countdown or lock that already ended before this ran needs no UI; its own Unlocked change cleans up.
        if (state != LockState.Unlocked && state != _machine.State)
        {
            UpdateTray();
            return;
        }
        switch (state)
        {
            case LockState.CountingDown:
                _settingsForm?.Close();
                if (!TryInstallHooks()) return;
                _badges.ShowCountdown(_machine.SecondsLeft);
                _countdownTimer.Start();
                StartWatchdog();
                break;

            case LockState.Locked:
                _countdownTimer.Stop();
                _settingsForm?.Close();
                if (!TryInstallHooks()) return;
                _hooks.Stats.Reset();
                SystemState.KeepAwake(true);
                if (_machine.Mode == LockMode.Cleaning)
                    _cleaning.Show(_settings.CleaningAutoEndSeconds, CleaningHint());
                _badges.ShowLocked(_machine.Mode, visible: _settings.ShowBadge && _machine.Mode != LockMode.Cleaning);
                StartWatchdog();
                _wasLocked = true;
                if (_settings.SoundEnabled) Chimes.PlayLock();
                break;

            case LockState.Unlocked:
                StopLockUi();
                if (_wasLocked)
                {
                    Diag.Log($"lock session {_hooks.Stats}");
                    if (_settings.SoundEnabled) Chimes.PlayUnlock();
                    _wasLocked = false;
                }
                break;
        }
        UpdateTray();
    }

    void StopLockUi()
    {
        _countdownTimer.Stop();
        _watchdogTimer.Stop();
        _holdTimer.Stop();
        _hooks.Uninstall();
        SystemState.KeepAwake(false);
        _badges.Hide();
        _cleaning.Hide();
    }

    string CleaningHint() => _settings.UnlockHoldSeconds > 0
        ? $"To finish, type your unlock word or hold {_settings.LockHotkey} for {_settings.UnlockHoldSeconds} seconds."
        : "To finish, type your unlock word.";

    bool TryInstallHooks()
    {
        try
        {
            _hooks.Install();
            return true;
        }
        catch (Win32Exception e)
        {
            Diag.Log("hook install failed: " + e.Message);
            _tray.Notify("Couldn't lock", "Windows refused the input hook: " + e.Message);
            _machine.ForceUnlock(LockReason.HookFailed);
            return false;
        }
    }

    void OnWordProgress(int typed)
    {
        if (_settings.ShowTypingProgress && _machine.State == LockState.Locked)
            _badges.SetDots(typed);
    }

    void OnHoldingChanged(bool holding)
    {
        if (holding && _machine.State == LockState.Locked)
        {
            _holdTimer.Start();
            _badges.SetHold(true, _gate.HoldProgress);
            return;
        }
        _holdTimer.Stop();
        _badges.SetHold(false, 0);
    }

    /// <summary>Every 50 ms while the hotkey is held: completes the hold even without key auto-repeat.</summary>
    void PollHold()
    {
        _gate.Poll();
        if (_machine.State == LockState.Locked)
            _badges.SetHold(true, _gate.HoldProgress);
    }

    void StartWatchdog()
    {
        _watchdogTicks = 0;
        _secureDesktopMisses = 0;
        _watchdogTimer.Start();
    }

    /// <summary>Every 250 ms while counting down or locked.</summary>
    void Watchdog()
    {
        // Ctrl+Alt+Del can't be blocked by any app. When its screen shows, step aside so you're never stuck.
        if (SystemState.IsDefaultDesktopActive())
        {
            _secureDesktopMisses = 0;
        }
        else if (++_secureDesktopMisses >= SecureDesktopMissesToUnlock)
        {
            _machine.ForceUnlock(LockReason.SecureDesktop);
            return;
        }

        if (_machine.State == LockState.Locked && ++_watchdogTicks % RehookEveryTicks == 0)
        {
            try
            {
                _hooks.Reinstall();
                Diag.Log("rehooked");
            }
            catch (Win32Exception e)
            {
                Diag.Log("rehook failed, keeping old hooks: " + e.Message);
            }
        }
        _cleaning.EnsureTopmost();
        _badges.EnsureTopmost();
    }

    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Post(() => _machine.ForceUnlock(LockReason.SessionLocked));
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Post(() =>
    {
        Diag.Log("displays changed");
        _badges.Rebuild();
        _cleaning.Rebuild();
    });

    void CheckIdle()
    {
        uint idleMs = SystemState.IdleMilliseconds();
        uint limitMs = (uint)_settings.IdleLockMinutes * 60_000;
        if (idleMs < limitMs)
        {
            _idleFired = false;
            return;
        }
        if (_idleFired || _machine.State != LockState.Unlocked || _settingsForm is not null || _prompting) return;
        _idleFired = true;
        _machine.IdleElapsed(_settings.CountdownSeconds, _settings.DefaultLockMode);
    }

    void ApplyIdleTimer()
    {
        _idleFired = false;
        _idleTimer.Enabled = _settings.IdleLockMinutes > 0;
    }

    // ---- hotkey, pause and settings ---------------------------------------------------------------------------

    void ApplyGateSettings()
    {
        _gate.SetUnlockWord(_settings.UnlockWord);
        _gate.SetHotkey(_settings.ParsedHotkey);
        _gate.SetHoldSeconds(_settings.UnlockHoldSeconds);
    }

    void RegisterHotkey()
    {
        _hotkeyRegistered = !_machine.Paused && _hotkeyWindow.Register(_settings.ParsedHotkey);
    }

    void TogglePause()
    {
        bool pause = !_machine.Paused;
        if (pause && !Authorized("pause the lock hotkey")) return;
        _machine.Paused = pause;
        if (pause) _hotkeyWindow.Unregister();
        else RegisterHotkey();
        UpdateTray();
    }

    /// <summary>When "Ask for the unlock word" is on, only someone who knows it gets past here.</summary>
    bool Authorized(string action)
    {
        if (!_settings.RequireWordForSettings) return true;
        if (_prompting) return false;
        _prompting = true;
        try
        {
            using var prompt = new WordPromptForm(_settings.UnlockWord, action);
            return prompt.ShowDialog() == DialogResult.OK && _machine.State == LockState.Unlocked;
        }
        finally
        {
            _prompting = false;
        }
    }

    void OpenSettings()
    {
        if (_machine.State != LockState.Unlocked) return;
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }
        if (!Authorized("open Settings") || _settingsForm is not null) return;

        // Free the hotkey while the window is open, so you can press it in the hotkey box.
        _hotkeyWindow.Unregister();
        bool startAtLogin = StartupTask.RegisteredCommand() is not null;
        _settingsForm = new SettingsForm(_settings, startAtLogin, choice => ApplySettings(choice, startAtLogin));
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm?.Dispose();
            _settingsForm = null;
            if (_exiting) return;
            RegisterHotkey();
            UpdateTray();
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    string? ApplySettings(SettingsChoice choice, bool wasStartAtLogin)
    {
        var settings = choice.Settings;
        if (!_machine.Paused)
        {
            // Test the new hotkey now so the settings window can say if another app owns it.
            if (!_hotkeyWindow.Register(settings.ParsedHotkey))
                return $"{settings.LockHotkey} is already used by another app. Pick a different hotkey.";
            _hotkeyWindow.Unregister();
        }

        if (choice.StartAtLogin != wasStartAtLogin)
        {
            string? error = choice.StartAtLogin ? StartupTask.Enable(Environment.ProcessPath!) : StartupTask.Disable();
            if (error is not null) return error;
        }

        bool adminTurnedOn = settings.RunAsAdmin && !_settings.RunAsAdmin;
        SaveSettings(settings);
        ApplyGateSettings();
        ApplyIdleTimer();
        if (adminTurnedOn && !Elevation.IsElevated) Post(RestartElevated);
        return null;
    }

    /// <summary>Starts an elevated copy that takes over from this one. Windows shows its admin prompt.</summary>
    void RestartElevated()
    {
        if (Elevation.IsElevated) return;
        if (!Elevation.SelfElevateEnabled)
        {
            Diag.Log("restart elevated skipped: this build doesn't elevate");
            return;
        }
        SaveSettings(_settings with { RunAsAdmin = true });
        var result = Elevation.Relaunch([.. _options.PassThrough, "--no-elevate", "--replace", Environment.ProcessId.ToString()]);
        Diag.Log($"restart elevated result={result}");
        switch (result)
        {
            case RelaunchResult.Started:
                ExitThread();
                break;
            case RelaunchResult.Declined:
                SaveSettings(_settings with { RunAsAdmin = false });
                break;
            case RelaunchResult.Failed:
                _tray.Notify("Couldn't restart with admin rights", "ChildLock keeps running without them.");
                break;
        }
    }

    void SaveSettings(Settings settings)
    {
        _settings = settings.Normalize();
        try
        {
            SettingsStore.Save(_options.SettingsPath, _settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _tray.Notify("Couldn't save settings", e.Message);
        }
        UpdateTray();
    }

    /// <summary>If the exe moved since "Start at sign-in" was turned on, point it at the new place.</summary>
    void RepairStartupTask()
    {
        try
        {
            StartupTask.Repair(Environment.ProcessPath!);
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Diag.Log("startup task check failed: " + e.Message);
        }
    }

    // ---- tray -------------------------------------------------------------------------------------------------

    Menu BuildMenu()
    {
        bool Unlocked() => _menu.State == LockState.Unlocked;
        return new Menu(
            new Item(() => _menu.State switch
            {
                LockState.Locked => "Locked",
                LockState.CountingDown => "Locking...",
                _ => "Lock now" + _menu.Accelerator,
            }, () => Post(() => Lock(_settings.DefaultLockMode))) { EnabledWhen = Unlocked },
            new Item("Lock only", new Menu(
                new Item("Keyboard and mouse", () => Post(() => Lock(LockMode.Full))),
                new Item("Keyboard", () => Post(() => Lock(LockMode.KeyboardOnly))),
                new Item("Mouse", () => Post(() => Lock(LockMode.MouseOnly))))) { EnabledWhen = Unlocked },
            new Item("Cleaning mode", () => Post(() => Lock(LockMode.Cleaning))) { EnabledWhen = Unlocked },
            Item.Separator,
            new Item("Settings...", () => Post(OpenSettings)) { EnabledWhen = Unlocked },
            new Item("Pause hotkey", () => Post(TogglePause)) { CheckedWhen = () => _menu.Paused, EnabledWhen = Unlocked },
            Item.Separator,
            new Item("Exit", () => Post(() =>
            {
                if (_machine.State == LockState.Unlocked && Authorized("exit ChildLock")) ExitThread();
            })) { EnabledWhen = Unlocked });
    }

    MenuState SnapshotMenu() =>
        new(_machine.State, _machine.Paused, _hotkeyRegistered ? "\t" + _settings.LockHotkey : null);

    static MenuTheme MenuThemeFor(SystemColorMode mode) => mode switch
    {
        SystemColorMode.Dark => MenuTheme.Dark,
        SystemColorMode.Classic => MenuTheme.Light,
        _ => MenuTheme.Auto,
    };

    string TooltipText()
    {
        string status = _machine.State switch
        {
            LockState.Locked => _machine.Mode switch
            {
                LockMode.KeyboardOnly => "keyboard locked",
                LockMode.MouseOnly => "mouse locked",
                LockMode.Cleaning => "cleaning mode",
                _ => "locked",
            },
            LockState.CountingDown => "locking...",
            _ when _machine.Paused => "hotkey paused",
            _ => "ready",
        };
        string lockLine = _machine.Paused ? "Lock from this menu"
            : _hotkeyRegistered || _settingsForm is not null ? $"Lock: {_settings.LockHotkey}"
            : "Lock hotkey unavailable";
        string text = $"ChildLock: {status}\n{lockLine}";
        return _settings.ShowWordInTooltip ? text + $"\nUnlock word: {_settings.UnlockWord}" : text;
    }

    void UpdateTray()
    {
        _menu = SnapshotMenu();
        var icon = _machine.State == LockState.Locked ? _lockedIcon : _machine.Paused ? _pausedIcon : _readyIcon;
        _tray?.Update(icon, TooltipText());
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _machine.ForceUnlock(LockReason.Exit);
        StopLockUi();
        _hooks.Dispose();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _countdownTimer.Dispose();
        _watchdogTimer.Dispose();
        _idleTimer.Dispose();
        _holdTimer.Dispose();
        _hotkeyWindow.Dispose();
        _settingsForm?.Close();
        _badges.Dispose();
        _cleaning.Dispose();
        _tray.Dispose();
        Diag.Log("exit");
        base.ExitThreadCore();
    }
}

using System.ComponentModel;
using ChildLock.Core;
using ChildLock.Native;
using Microsoft.Win32;

namespace ChildLock;

/// <summary>The running app: tray icon, hotkey, hooks, badge and the timers that drive them.</summary>
internal sealed class TrayContext : ApplicationContext
{
    const int RehookEveryTicks = 20; // 20 x 250 ms = 5 s
    const int SecureDesktopMissesToUnlock = 2;

    readonly AppOptions _options;
    readonly SynchronizationContext _ui;
    readonly LockStateMachine _machine = new();
    readonly InputGate _gate;
    readonly InputHooks _hooks;
    readonly HotkeyWindow _hotkeyWindow;
    readonly BadgeForm _badge = new();
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _lockItem;
    readonly ToolStripMenuItem _pauseItem;
    readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _watchdogTimer = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 5000 };

    Settings _settings;
    SettingsForm? _settingsForm;
    bool _hotkeyRegistered;
    bool _idleFired;
    int _watchdogTicks;
    int _secureDesktopMisses;
    bool _wasLocked;
    Icon? _icon;

    public TrayContext(AppOptions options)
    {
        _options = options;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("No UI context");
        _settings = SettingsStore.Load(options.SettingsPath);

        _gate = new InputGate(_machine, _settings.UnlockWord, SystemState.HeldInput);
        _hooks = new InputHooks(_gate);

        // State changes can happen inside a hook callback; defer the UI work so the callback returns fast.
        _machine.StateChanged += (state, reason) => _ui.Post(_ => OnStateChanged(state, reason), null);
        _machine.CountdownChanged += seconds => _ui.Post(_ => _badge.ShowCountdown(seconds), null);

        _hotkeyWindow = new HotkeyWindow(options.MutexName);
        _hotkeyWindow.Pressed += () => _machine.HotkeyPressed(_settings.CountdownSeconds);
        _hotkeyWindow.ShowSettingsRequested += () =>
        {
            if (_machine.State == LockState.Unlocked) OpenSettings();
        };
        _countdownTimer.Tick += (_, _) => _machine.Tick();
        _watchdogTimer.Tick += (_, _) => Watchdog();
        _idleTimer.Tick += (_, _) => CheckIdle();
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _lockItem = new ToolStripMenuItem("Lock now", null, (_, _) => _machine.TrayLock(_settings.CountdownSeconds));
        _pauseItem = new ToolStripMenuItem("Pause hotkey", null, (_, _) => SetPaused(!_machine.Paused));
        var menu = new ContextMenuStrip();
        menu.Items.Add(_lockItem);
        menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => OpenSettings()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };

        RegisterHotkey();
        ApplyIdleTimer();
        UpdateTray();
        Diag.Log($"started settings={options.SettingsPath} hotkey={_settings.LockHotkey} registered={_hotkeyRegistered} default_desktop={SystemState.IsDefaultDesktopActive()}");

        if (!_hotkeyRegistered)
            Balloon("Lock hotkey unavailable",
                $"{_settings.LockHotkey} is already used by another app. Pick a different hotkey in Settings.", ToolTipIcon.Warning);
        else if (!_settings.FirstRunDone)
            Balloon("ChildLock is running",
                $"Press {_settings.LockHotkey} to lock the keyboard and mouse. Type \"{_settings.UnlockWord}\" to unlock.", ToolTipIcon.Info);

        if (!_settings.FirstRunDone)
            SaveSettings(_settings with { FirstRunDone = true });

        if (!options.CustomSettingsPath)
            Task.Run(RepairStartupTask);
    }

    void OnStateChanged(LockState state, LockReason reason)
    {
        Diag.Log($"state={state} reason={reason}");
        switch (state)
        {
            case LockState.CountingDown:
                if (!TryInstallHooks()) return;
                _badge.ShowCountdown(_machine.SecondsLeft);
                _countdownTimer.Start();
                StartWatchdog();
                break;

            case LockState.Locked:
                _countdownTimer.Stop();
                if (!TryInstallHooks()) return;
                _hooks.Stats.Reset();
                SystemState.KeepAwake(true);
                _badge.ShowLocked();
                StartWatchdog();
                _wasLocked = true;
                if (_settings.SoundEnabled) Chimes.PlayLock();
                break;

            case LockState.Unlocked:
                _countdownTimer.Stop();
                _watchdogTimer.Stop();
                _hooks.Uninstall();
                SystemState.KeepAwake(false);
                _badge.Hide();
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
            Balloon("Couldn't lock", "Windows refused the input hook: " + e.Message, ToolTipIcon.Error);
            _machine.ForceUnlock(LockReason.Exit);
            return false;
        }
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
        _badge.EnsureTopmost();
    }

    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _ui.Post(_ => _machine.ForceUnlock(LockReason.SessionLocked), null);
    }

    void CheckIdle()
    {
        uint idleMs = SystemState.IdleMilliseconds();
        uint limitMs = (uint)_settings.IdleLockMinutes * 60_000;
        if (idleMs < limitMs)
        {
            _idleFired = false;
            return;
        }
        if (_idleFired || _machine.State != LockState.Unlocked || _settingsForm is not null) return;
        _idleFired = true;
        _machine.IdleElapsed(_settings.CountdownSeconds);
    }

    void ApplyIdleTimer()
    {
        _idleFired = false;
        _idleTimer.Enabled = _settings.IdleLockMinutes > 0;
    }

    void RegisterHotkey()
    {
        _hotkeyRegistered = !_machine.Paused && _hotkeyWindow.Register(_settings.ParsedHotkey);
    }

    void SetPaused(bool paused)
    {
        _machine.Paused = paused;
        if (paused) _hotkeyWindow.Unregister();
        else RegisterHotkey();
        UpdateTray();
    }

    void OpenSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        // Free the hotkey while the window is open, so you can press it in the hotkey box.
        _hotkeyWindow.Unregister();
        bool startAtLogin = StartupTask.RegisteredCommand() is not null;
        _settingsForm = new SettingsForm(_settings, startAtLogin, choice => ApplySettings(choice, startAtLogin));
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
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

        _gate.SetUnlockWord(settings.UnlockWord);
        SaveSettings(settings);
        ApplyIdleTimer();
        return null;
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
            Balloon("Couldn't save settings", e.Message, ToolTipIcon.Warning);
        }
    }

    /// <summary>If the exe moved since "Start at login" was turned on, point the task at the new place.</summary>
    void RepairStartupTask()
    {
        try
        {
            string? registered = StartupTask.RegisteredCommand();
            string current = Environment.ProcessPath!;
            if (registered is not null && !string.Equals(registered, current, StringComparison.OrdinalIgnoreCase))
            {
                string? error = StartupTask.Enable(current);
                Diag.Log($"startup task repointed error={error ?? "none"}");
            }
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            Diag.Log("startup task check failed: " + e.Message);
        }
    }

    void UpdateTray()
    {
        bool locked = _machine.State == LockState.Locked;
        var old = _icon;
        _icon = IconFactory.TrayIcon(locked, _machine.Paused);
        _tray.Icon = _icon;
        old?.Dispose();

        string status = _machine.State switch
        {
            LockState.Locked => "locked",
            LockState.CountingDown => "locking...",
            _ when _machine.Paused => "hotkey paused",
            _ => "ready",
        };
        string lockLine = _machine.Paused ? "Lock from this menu"
            : _hotkeyRegistered || _settingsForm is not null ? $"Lock: {_settings.LockHotkey}"
            : "Lock hotkey unavailable";
        _tray.Text = $"ChildLock: {status}\n{lockLine}\nUnlock word: {_settings.UnlockWord}";

        _lockItem.Enabled = _machine.State == LockState.Unlocked;
        _lockItem.ShortcutKeyDisplayString = _hotkeyRegistered ? _settings.LockHotkey : null;
        _pauseItem.Checked = _machine.Paused;
    }

    void Balloon(string title, string text, ToolTipIcon icon) => _tray.ShowBalloonTip(8000, title, text, icon);

    protected override void ExitThreadCore()
    {
        _machine.ForceUnlock(LockReason.Exit);
        _hooks.Dispose();
        SystemState.KeepAwake(false);
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _countdownTimer.Dispose();
        _watchdogTimer.Dispose();
        _idleTimer.Dispose();
        _hotkeyWindow.Dispose();
        _settingsForm?.Close();
        _badge.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icon?.Dispose();
        Diag.Log("exit");
        base.ExitThreadCore();
    }
}

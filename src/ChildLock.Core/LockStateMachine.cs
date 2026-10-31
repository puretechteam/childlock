namespace ChildLock.Core;

public enum LockState
{
    Unlocked,
    CountingDown,
    Locked,
}

/// <summary>What a lock blocks.</summary>
public enum LockMode
{
    /// <summary>Keyboard and mouse.</summary>
    Full,
    KeyboardOnly,
    MouseOnly,
    /// <summary>Keyboard and mouse, with every screen blacked out for wiping.</summary>
    Cleaning,
}

public static class LockModeExtensions
{
    public static bool BlocksKeyboard(this LockMode mode) => mode != LockMode.MouseOnly;
    public static bool BlocksMouse(this LockMode mode) => mode != LockMode.KeyboardOnly;

    /// <summary>Modes you can pick as the default for the hotkey and auto-lock.</summary>
    public static bool IsDefaultable(this LockMode mode) =>
        mode is LockMode.Full or LockMode.KeyboardOnly or LockMode.MouseOnly;
}

public enum LockReason
{
    Hotkey,
    Tray,
    Idle,
    CountdownFinished,
    Cancelled,
    UnlockWord,
    HoldHotkey,
    CleaningFinished,
    SecureDesktop,
    SessionLocked,
    HookFailed,
    Exit,
}

/// <summary>
/// Unlocked -> CountingDown -> Locked -> Unlocked. All calls come from the UI thread.
/// </summary>
public sealed class LockStateMachine
{
    public LockState State { get; private set; } = LockState.Unlocked;

    /// <summary>What the current (or last) countdown or lock blocks.</summary>
    public LockMode Mode { get; private set; } = LockMode.Full;

    /// <summary>Seconds left while counting down.</summary>
    public int SecondsLeft { get; private set; }

    /// <summary>When paused, the hotkey and idle auto-lock do nothing. Tray lock still works.</summary>
    public bool Paused { get; set; }

    public event Action<LockState, LockReason>? StateChanged;
    public event Action<int>? CountdownChanged;

    /// <summary>The lock hotkey starts a lock, or cancels a running countdown.</summary>
    public void HotkeyPressed(int countdownSeconds, LockMode mode = LockMode.Full)
    {
        if (State == LockState.CountingDown)
            Set(LockState.Unlocked, LockReason.Cancelled);
        else if (State == LockState.Unlocked && !Paused)
            Begin(countdownSeconds, mode, LockReason.Hotkey);
    }

    public void TrayLock(int countdownSeconds, LockMode mode = LockMode.Full)
    {
        if (State == LockState.Unlocked)
            Begin(countdownSeconds, mode, LockReason.Tray);
    }

    public void IdleElapsed(int countdownSeconds, LockMode mode = LockMode.Full)
    {
        if (State == LockState.Unlocked && !Paused)
            Begin(countdownSeconds, mode, LockReason.Idle);
    }

    public void Escape()
    {
        if (State == LockState.CountingDown)
            Set(LockState.Unlocked, LockReason.Cancelled);
    }

    /// <summary>Called once a second while counting down.</summary>
    public void Tick()
    {
        if (State != LockState.CountingDown) return;
        SecondsLeft--;
        if (SecondsLeft <= 0)
            Set(LockState.Locked, LockReason.CountdownFinished);
        else
            CountdownChanged?.Invoke(SecondsLeft);
    }

    public void WordMatched()
    {
        if (State == LockState.Locked)
            Set(LockState.Unlocked, LockReason.UnlockWord);
    }

    /// <summary>The lock hotkey was held long enough while the keyboard is locked.</summary>
    public void HoldCompleted()
    {
        if (State == LockState.Locked && Mode.BlocksKeyboard())
            Set(LockState.Unlocked, LockReason.HoldHotkey);
    }

    /// <summary>The lock hotkey was pressed during a mouse-only lock, where the keyboard still works.</summary>
    public void HotkeyUnlock()
    {
        if (State == LockState.Locked && Mode == LockMode.MouseOnly)
            Set(LockState.Unlocked, LockReason.Hotkey);
    }

    /// <summary>Cleaning mode's timer ran out.</summary>
    public void CleaningTimeUp()
    {
        if (State == LockState.Locked && Mode == LockMode.Cleaning)
            Set(LockState.Unlocked, LockReason.CleaningFinished);
    }

    /// <summary>Failsafe unlock (Ctrl+Alt+Del screen, Windows lock, hook failure, exit).</summary>
    public void ForceUnlock(LockReason reason)
    {
        if (State != LockState.Unlocked)
            Set(LockState.Unlocked, reason);
    }

    void Begin(int countdownSeconds, LockMode mode, LockReason reason)
    {
        Mode = mode;
        countdownSeconds = Math.Clamp(countdownSeconds, 0, Settings.MaxCountdownSeconds);
        if (countdownSeconds == 0)
        {
            Set(LockState.Locked, reason);
            return;
        }
        SecondsLeft = countdownSeconds;
        Set(LockState.CountingDown, reason);
    }

    void Set(LockState state, LockReason reason)
    {
        if (state != LockState.CountingDown) SecondsLeft = 0;
        State = state;
        StateChanged?.Invoke(state, reason);
    }
}

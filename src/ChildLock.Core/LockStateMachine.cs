namespace ChildLock.Core;

public enum LockState
{
    Unlocked,
    CountingDown,
    Locked,
}

public enum LockReason
{
    Hotkey,
    Tray,
    Idle,
    CountdownFinished,
    Cancelled,
    UnlockWord,
    SecureDesktop,
    SessionLocked,
    Exit,
}

/// <summary>
/// Unlocked -> CountingDown -> Locked -> Unlocked. All calls come from the UI thread.
/// </summary>
public sealed class LockStateMachine
{
    public LockState State { get; private set; } = LockState.Unlocked;

    /// <summary>Seconds left while counting down.</summary>
    public int SecondsLeft { get; private set; }

    /// <summary>When paused, the hotkey and idle auto-lock do nothing. Tray lock still works.</summary>
    public bool Paused { get; set; }

    public event Action<LockState, LockReason>? StateChanged;
    public event Action<int>? CountdownChanged;

    /// <summary>The lock hotkey starts a lock, or cancels a running countdown.</summary>
    public void HotkeyPressed(int countdownSeconds)
    {
        if (State == LockState.CountingDown)
            Set(LockState.Unlocked, LockReason.Cancelled);
        else if (State == LockState.Unlocked && !Paused)
            Begin(countdownSeconds, LockReason.Hotkey);
    }

    public void TrayLock(int countdownSeconds)
    {
        if (State == LockState.Unlocked)
            Begin(countdownSeconds, LockReason.Tray);
    }

    public void IdleElapsed(int countdownSeconds)
    {
        if (State == LockState.Unlocked && !Paused)
            Begin(countdownSeconds, LockReason.Idle);
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

    /// <summary>Failsafe unlock (Ctrl+Alt+Del screen, Windows lock, exit).</summary>
    public void ForceUnlock(LockReason reason)
    {
        if (State != LockState.Unlocked)
            Set(LockState.Unlocked, reason);
    }

    void Begin(int countdownSeconds, LockReason reason)
    {
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

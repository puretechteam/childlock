namespace ChildLock.Tests;

public class LockStateMachineTests
{
    readonly LockStateMachine _m = new();
    readonly List<(LockState State, LockReason Reason)> _changes = [];
    readonly List<int> _countdown = [];

    public LockStateMachineTests()
    {
        _m.StateChanged += (s, r) => _changes.Add((s, r));
        _m.CountdownChanged += _countdown.Add;
    }

    [Fact]
    public void Starts_unlocked()
    {
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Zero_countdown_locks_immediately()
    {
        _m.HotkeyPressed(0);
        Assert.Equal(LockState.Locked, _m.State);
        Assert.Equal([(LockState.Locked, LockReason.Hotkey)], _changes);
    }

    [Fact]
    public void Countdown_ticks_down_then_locks()
    {
        _m.TrayLock(3);
        Assert.Equal(LockState.CountingDown, _m.State);
        Assert.Equal(3, _m.SecondsLeft);
        _m.Tick();
        _m.Tick();
        Assert.Equal([2, 1], _countdown);
        Assert.Equal(LockState.CountingDown, _m.State);
        _m.Tick();
        Assert.Equal(LockState.Locked, _m.State);
        Assert.Equal(0, _m.SecondsLeft);
        Assert.Equal(
            [(LockState.CountingDown, LockReason.Tray), (LockState.Locked, LockReason.CountdownFinished)],
            _changes);
    }

    [Fact]
    public void Escape_cancels_countdown()
    {
        _m.HotkeyPressed(3);
        _m.Escape();
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.Equal(LockReason.Cancelled, _changes[^1].Reason);
        _m.Tick();
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Hotkey_again_cancels_countdown()
    {
        _m.HotkeyPressed(3);
        _m.HotkeyPressed(3);
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.Equal(LockReason.Cancelled, _changes[^1].Reason);
    }

    [Fact]
    public void Hotkey_and_escape_do_nothing_while_locked()
    {
        _m.HotkeyPressed(0);
        _m.HotkeyPressed(0);
        _m.Escape();
        _m.TrayLock(0);
        _m.IdleElapsed(0);
        Assert.Equal(LockState.Locked, _m.State);
        Assert.Single(_changes);
    }

    [Fact]
    public void Word_unlocks_only_when_locked()
    {
        _m.WordMatched();
        Assert.Empty(_changes);
        _m.HotkeyPressed(2);
        _m.WordMatched();
        Assert.Equal(LockState.CountingDown, _m.State);
        _m.Tick();
        _m.Tick();
        _m.WordMatched();
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.Equal(LockReason.UnlockWord, _changes[^1].Reason);
    }

    [Theory]
    [InlineData(LockReason.SecureDesktop)]
    [InlineData(LockReason.SessionLocked)]
    [InlineData(LockReason.Exit)]
    public void Force_unlock_works_from_locked_and_countdown(LockReason reason)
    {
        _m.HotkeyPressed(0);
        _m.ForceUnlock(reason);
        Assert.Equal((LockState.Unlocked, reason), _changes[^1]);

        _m.HotkeyPressed(5);
        _m.ForceUnlock(reason);
        Assert.Equal((LockState.Unlocked, reason), _changes[^1]);

        int before = _changes.Count;
        _m.ForceUnlock(reason);
        Assert.Equal(before, _changes.Count);
    }

    [Fact]
    public void Pause_blocks_hotkey_and_idle_but_not_tray()
    {
        _m.Paused = true;
        _m.HotkeyPressed(0);
        _m.IdleElapsed(0);
        Assert.Equal(LockState.Unlocked, _m.State);
        _m.TrayLock(0);
        Assert.Equal(LockState.Locked, _m.State);
    }

    [Fact]
    public void Pause_still_lets_hotkey_cancel_a_countdown()
    {
        _m.TrayLock(3);
        _m.Paused = true;
        _m.HotkeyPressed(3);
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Idle_starts_countdown()
    {
        _m.IdleElapsed(3);
        Assert.Equal((LockState.CountingDown, LockReason.Idle), _changes[^1]);
    }

    [Theory]
    [InlineData(-5, LockState.Locked)]
    [InlineData(99, LockState.CountingDown)]
    public void Countdown_is_clamped(int seconds, LockState expected)
    {
        _m.TrayLock(seconds);
        Assert.Equal(expected, _m.State);
        Assert.InRange(_m.SecondsLeft, 0, Settings.MaxCountdownSeconds);
    }
}

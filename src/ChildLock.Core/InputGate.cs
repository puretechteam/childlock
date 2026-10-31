namespace ChildLock.Core;

public enum MouseAction
{
    Move,
    ButtonDown,
    ButtonUp,
    Wheel,
}

/// <summary>
/// Decides, per input event, whether to block it. This is what the low-level hooks call,
/// so it must stay fast and allocation-free.
/// </summary>
public sealed class InputGate
{
    readonly LockStateMachine _machine;
    readonly Func<IEnumerable<int>> _heldInputSnapshot;
    readonly Func<long> _clock;
    readonly HeldKeyTracker _held = new();
    readonly HotkeyChordTracker _chord = new();
    readonly bool[] _down = new bool[256];     // keys pressed during the lock that Windows never saw go down
    readonly bool[] _swallow = new bool[256];  // the same keys after unlocking, until they are released
    UnlockWordMatcher _matcher;
    int _holdMs = Settings.DefaultHoldSeconds * 1000;
    int _reportedProgress;
    bool _reportedHolding;

    /// <param name="heldInputSnapshot">Returns the virtual keys and mouse buttons currently down.</param>
    /// <param name="clock">Milliseconds from any fixed point; defaults to <see cref="Environment.TickCount64"/>.</param>
    public InputGate(LockStateMachine machine, string unlockWord, Func<IEnumerable<int>> heldInputSnapshot,
        Func<long>? clock = null)
    {
        _machine = machine;
        _heldInputSnapshot = heldInputSnapshot;
        _clock = clock ?? (() => Environment.TickCount64);
        _matcher = new UnlockWordMatcher(unlockWord);
        _machine.StateChanged += OnStateChanged;
    }

    /// <summary>Characters of the unlock word typed so far while locked. Raised from inside the hook.</summary>
    public event Action<int>? WordProgressChanged;

    /// <summary>The lock hotkey started or stopped being held while locked. Raised from inside the hook.</summary>
    public event Action<bool>? HoldingChanged;

    public void SetUnlockWord(string word) => _matcher = new UnlockWordMatcher(word);

    public void SetHotkey(Hotkey hotkey) => _chord.SetHotkey(hotkey);

    /// <summary>How long to hold the lock hotkey to unlock; 0 turns it off.</summary>
    public void SetHoldSeconds(int seconds) => _holdMs = Math.Max(0, seconds) * 1000;

    public int WordLength => _matcher.Length;

    /// <summary>0 to 1 while the hotkey is being held to unlock, otherwise 0.</summary>
    public double HoldProgress =>
        HoldCounts ? Math.Min(1, (double)_chord.HeldFor(_clock()) / _holdMs) : 0;

    bool HoldCounts =>
        _holdMs > 0 && _chord.Holding && _machine.State == LockState.Locked && _machine.Mode.BlocksKeyboard();

    void OnStateChanged(LockState state, LockReason reason)
    {
        if (state == LockState.Locked)
        {
            var held = _heldInputSnapshot().ToArray();
            _held.Reset(held);
            _chord.Reset(held);
            _matcher.Reset();
            Array.Clear(_down);
            Array.Clear(_swallow);
            _reportedProgress = 0;
            _reportedHolding = false;
        }
        else if (state == LockState.Unlocked)
        {
            // Keys still down at unlock would otherwise auto-repeat into whatever has focus.
            Array.Copy(_down, _swallow, _down.Length);
            Array.Clear(_down);
            ReportHolding();
        }
    }

    /// <summary>Returns true to block the key event.</summary>
    public bool OnKey(int vk, bool isUp)
    {
        switch (_machine.State)
        {
            case LockState.CountingDown:
                if (vk == VirtualKeys.Escape)
                {
                    if (!isUp) _machine.Escape();
                    return true;
                }
                return false;

            case LockState.Locked:
                long now = _clock();
                bool pressed = _chord.OnKey(vk, isUp, now);
                if (!_machine.Mode.BlocksKeyboard())
                {
                    // Mouse-only lock: the keyboard works, and the hotkey itself unlocks. Swallowing its
                    // key-down keeps RegisterHotKey from seeing it and starting a new lock straight away.
                    if (!pressed) return false;
                    _machine.HotkeyUnlock();
                    return true;
                }
                bool block = OnLockedKey(vk, isUp);
                if (_holdMs > 0 && _chord.Holding && _chord.HeldFor(now) >= _holdMs) _machine.HoldCompleted();
                ReportHolding();
                return block;

            default:
                if ((uint)vk < (uint)_swallow.Length && _swallow[vk])
                {
                    if (isUp) _swallow[vk] = false;
                    return true;
                }
                return false;
        }
    }

    bool OnLockedKey(int vk, bool isUp)
    {
        if (isUp)
        {
            if (_held.TryRelease(vk)) return false;
            if ((uint)vk < (uint)_down.Length) _down[vk] = false;
            return true;
        }
        // Auto-repeat of a key that was already down at lock time: block it, but it isn't typing.
        if (_held.IsHeld(vk)) return true;
        if ((uint)vk < (uint)_down.Length) _down[vk] = true;

        if (_matcher.Feed(vk))
        {
            ReportProgress(0);
            _machine.WordMatched();
            return true;
        }
        ReportProgress(_matcher.Progress);
        return true;
    }

    /// <summary>
    /// Call a few times a second while the hotkey is held, because not every keyboard auto-repeats.
    /// </summary>
    public void Poll()
    {
        if (HoldCounts && _chord.HeldFor(_clock()) >= _holdMs) _machine.HoldCompleted();
        ReportHolding();
    }

    void ReportProgress(int progress)
    {
        if (progress == _reportedProgress) return;
        _reportedProgress = progress;
        WordProgressChanged?.Invoke(progress);
    }

    void ReportHolding()
    {
        bool holding = HoldCounts;
        if (holding == _reportedHolding) return;
        _reportedHolding = holding;
        HoldingChanged?.Invoke(holding);
    }

    /// <summary>Returns true to block the mouse event.</summary>
    /// <param name="buttonVk">VK of the button for button events, otherwise 0.</param>
    public bool OnMouse(MouseAction action, int buttonVk)
    {
        if (_machine.State != LockState.Locked || !_machine.Mode.BlocksMouse()) return false;
        if (action == MouseAction.ButtonUp && _held.TryRelease(buttonVk)) return false;
        return true;
    }
}

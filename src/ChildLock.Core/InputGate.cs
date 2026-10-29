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
    readonly HeldKeyTracker _held = new();
    UnlockWordMatcher _matcher;

    /// <param name="heldInputSnapshot">Returns the virtual keys and mouse buttons currently down.</param>
    public InputGate(LockStateMachine machine, string unlockWord, Func<IEnumerable<int>> heldInputSnapshot)
    {
        _machine = machine;
        _heldInputSnapshot = heldInputSnapshot;
        _matcher = new UnlockWordMatcher(unlockWord);
        _machine.StateChanged += OnStateChanged;
    }

    public void SetUnlockWord(string word) => _matcher = new UnlockWordMatcher(word);

    void OnStateChanged(LockState state, LockReason reason)
    {
        if (state == LockState.Locked)
        {
            _held.Reset(_heldInputSnapshot());
            _matcher.Reset();
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
                if (isUp) return !_held.TryRelease(vk);
                if (_matcher.Feed(vk)) _machine.WordMatched();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Returns true to block the mouse event.</summary>
    /// <param name="buttonVk">VK of the button for button events, otherwise 0.</param>
    public bool OnMouse(MouseAction action, int buttonVk)
    {
        if (_machine.State != LockState.Locked) return false;
        if (action == MouseAction.ButtonUp && _held.TryRelease(buttonVk)) return false;
        return true;
    }
}

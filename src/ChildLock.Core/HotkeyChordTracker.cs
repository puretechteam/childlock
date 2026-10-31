namespace ChildLock.Core;

/// <summary>
/// Follows key events while locked to spot the lock hotkey being pressed, and how long it has been held.
/// The modifiers must match exactly, as with RegisterHotKey. A main key that was already down when the
/// lock started (its auto-repeat) never counts; it has to be released and pressed again.
/// Allocation-free: this runs inside the keyboard hook.
/// </summary>
public sealed class HotkeyChordTracker
{
    // One bit per modifier key: the generic code plus its left and right versions.
    const int ShiftBits = 0b000_000_111, CtrlBits = 0b000_111_000, AltBits = 0b111_000_000, WinBits = 0b11 << 9;

    Hotkey _hotkey = Hotkey.Default;
    int _down;
    bool _mainDown;
    bool _armed = true;
    bool _holding;
    long _holdSince;

    public void SetHotkey(Hotkey hotkey)
    {
        _hotkey = hotkey;
        Reset([]);
    }

    /// <summary>Starts fresh at lock time, from the keys and buttons physically down right now.</summary>
    public void Reset(IEnumerable<int> heldVks)
    {
        _down = 0;
        _mainDown = false;
        _armed = true;
        _holding = false;
        foreach (int vk in heldVks)
        {
            if (vk == _hotkey.Key) _armed = false;
            else if (Bit(vk) is var bit and not 0) _down |= bit;
        }
        // GetAsyncKeyState reports both VK_SHIFT and VK_LSHIFT; keep only the sided code the hook will release.
        DropGenericIfSided(ShiftBits);
        DropGenericIfSided(CtrlBits);
        DropGenericIfSided(AltBits);
    }

    /// <summary>True while the hotkey is held down and nothing else has been pressed since.</summary>
    public bool Holding => _holding;

    /// <summary>Milliseconds the hotkey has been held, or 0 when it isn't.</summary>
    public long HeldFor(long nowMs) => _holding ? Math.Max(0, nowMs - _holdSince) : 0;

    /// <summary>Feeds one key event. Returns true when this key-down completes the hotkey chord.</summary>
    public bool OnKey(int vk, bool isUp, long nowMs)
    {
        if (Bit(vk) is var bit and not 0)
        {
            if (isUp) Release(bit);
            else _down |= bit;
            if (_holding && CurrentModifiers() != _hotkey.Modifiers) _holding = false;
            return false;
        }

        if (vk == _hotkey.Key)
        {
            if (isUp)
            {
                _mainDown = false;
                _armed = true;
                _holding = false;
                return false;
            }
            if (!_armed || _mainDown) return false; // auto-repeat
            _mainDown = true;
            if (CurrentModifiers() != _hotkey.Modifiers) return false;
            _holding = true;
            _holdSince = nowMs;
            return true;
        }

        // Any other key cancels a hold, so mashing while one key happens to be down can't unlock.
        if (!isUp) _holding = false;
        return false;
    }

    public Modifiers CurrentModifiers()
    {
        var mods = Modifiers.None;
        if ((_down & ShiftBits) != 0) mods |= Modifiers.Shift;
        if ((_down & CtrlBits) != 0) mods |= Modifiers.Ctrl;
        if ((_down & AltBits) != 0) mods |= Modifiers.Alt;
        if ((_down & WinBits) != 0) mods |= Modifiers.Win;
        return mods;
    }

    void Release(int bit)
    {
        // Releasing the generic code (VK_SHIFT) releases both sides; releasing the last side releases the generic code.
        int group = GroupOf(bit);
        int generic = GenericOf(bit);
        if (bit == generic)
        {
            _down &= ~group;
            return;
        }
        _down &= ~bit;
        if ((_down & group & ~generic) == 0) _down &= ~generic;
    }

    void DropGenericIfSided(int group)
    {
        int generic = GenericOf(group);
        if ((_down & group & ~generic) != 0) _down &= ~generic;
    }

    static int GroupOf(int bit) =>
        (bit & ShiftBits) != 0 ? ShiftBits : (bit & CtrlBits) != 0 ? CtrlBits : (bit & AltBits) != 0 ? AltBits : WinBits;

    /// <summary>The bit of the generic code (VK_SHIFT, VK_CONTROL, VK_MENU) in a modifier's group; 0 for Win.</summary>
    static int GenericOf(int bitOrGroup) =>
        (bitOrGroup & ShiftBits) != 0 ? 1 << 0 : (bitOrGroup & CtrlBits) != 0 ? 1 << 3 : (bitOrGroup & AltBits) != 0 ? 1 << 6 : 0;

    static int Bit(int vk) => vk switch
    {
        VirtualKeys.Shift => 1 << 0,
        VirtualKeys.LShift => 1 << 1,
        VirtualKeys.RShift => 1 << 2,
        VirtualKeys.Control => 1 << 3,
        VirtualKeys.LControl => 1 << 4,
        VirtualKeys.RControl => 1 << 5,
        VirtualKeys.Menu => 1 << 6,
        VirtualKeys.LMenu => 1 << 7,
        VirtualKeys.RMenu => 1 << 8,
        VirtualKeys.LWin => 1 << 9,
        VirtualKeys.RWin => 1 << 10,
        _ => 0,
    };
}

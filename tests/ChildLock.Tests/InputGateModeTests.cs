namespace ChildLock.Tests;

/// <summary>Lock modes, hold-to-unlock, typing progress and what happens to keys still down at unlock.</summary>
public class InputGateModeTests
{
    const int LCtrl = VirtualKeys.LControl;
    const int LAlt = VirtualKeys.LMenu;
    const int LShift = VirtualKeys.LShift;
    const int L = VirtualKeys.L;

    readonly LockStateMachine _m = new();
    readonly InputGate _gate;
    readonly List<LockReason> _reasons = [];
    readonly List<int> _progress = [];
    readonly List<bool> _holding = [];
    int[] _heldAtLock = [];
    long _now = 1_000;

    public InputGateModeTests()
    {
        _gate = new InputGate(_m, "unlock", () => _heldAtLock, () => _now);
        _gate.SetHotkey(Hotkey.Default);
        _gate.SetHoldSeconds(3);
        _gate.WordProgressChanged += _progress.Add;
        _gate.HoldingChanged += _holding.Add;
        _m.StateChanged += (_, reason) => _reasons.Add(reason);
    }

    bool Press(int vk) => _gate.OnKey(vk, isUp: false);
    bool Release(int vk) => _gate.OnKey(vk, isUp: true);

    void PressChord()
    {
        Press(LCtrl);
        Press(LAlt);
        Press(L);
    }

    [Fact]
    public void Keyboard_only_blocks_keys_but_not_the_mouse()
    {
        _m.HotkeyPressed(0, LockMode.KeyboardOnly);
        Assert.True(Press(VirtualKeys.A));
        Assert.True(Release(VirtualKeys.A));
        Assert.False(_gate.OnMouse(MouseAction.Move, 0));
        Assert.False(_gate.OnMouse(MouseAction.ButtonDown, VirtualKeys.LButton));
        Assert.False(_gate.OnMouse(MouseAction.Wheel, 0));
    }

    [Fact]
    public void Mouse_only_blocks_the_mouse_but_not_keys()
    {
        _m.HotkeyPressed(0, LockMode.MouseOnly);
        Assert.False(Press(VirtualKeys.A));
        Assert.False(Release(VirtualKeys.A));
        Assert.True(_gate.OnMouse(MouseAction.Move, 0));
        Assert.True(_gate.OnMouse(MouseAction.ButtonDown, VirtualKeys.LButton));
    }

    [Fact]
    public void Mouse_only_unlocks_on_the_hotkey_and_swallows_it()
    {
        _m.HotkeyPressed(0, LockMode.MouseOnly);
        Assert.False(Press(LCtrl));
        Assert.False(Press(LAlt));
        Assert.True(Press(L)); // swallowed so RegisterHotKey doesn't start a new lock
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.Equal(LockReason.Hotkey, _reasons[^1]);
    }

    [Fact]
    public void Mouse_only_ignores_the_hotkey_still_held_from_locking()
    {
        _heldAtLock = [LCtrl, LAlt, L];
        _m.HotkeyPressed(0, LockMode.MouseOnly);
        Assert.False(Press(L)); // auto-repeat of the press that locked
        Assert.Equal(LockState.Locked, _m.State);
        Release(L);
        Assert.True(Press(L));
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Cleaning_mode_blocks_everything()
    {
        _m.TrayLock(0, LockMode.Cleaning);
        Assert.True(Press(VirtualKeys.A));
        Assert.True(_gate.OnMouse(MouseAction.Move, 0));
    }

    [Fact]
    public void Holding_the_hotkey_unlocks_after_the_hold_time()
    {
        _m.HotkeyPressed(0);
        PressChord();
        Assert.Equal([true], _holding);
        _now += 1_500;
        Assert.Equal(0.5, _gate.HoldProgress, 3);
        Assert.True(Press(L)); // auto-repeat
        Assert.Equal(LockState.Locked, _m.State);
        _now += 1_500;
        Assert.True(Press(L));
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.Equal(LockReason.HoldHotkey, _reasons[^1]);
        Assert.Equal([true, false], _holding);
    }

    [Fact]
    public void Poll_completes_a_hold_without_auto_repeat()
    {
        _m.HotkeyPressed(0);
        PressChord();
        _now += 2_999;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);
        _now += 1;
        _gate.Poll();
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Keys_still_held_after_a_hold_unlock_never_reach_the_app()
    {
        _m.HotkeyPressed(0);
        PressChord();
        _now += 3_000;
        _gate.Poll();
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.True(Press(L)); // auto-repeat would otherwise type "l"
        Assert.True(Release(L));
        Assert.True(Release(LAlt));
        Assert.True(Release(LCtrl));
        Assert.False(Press(L));
        Assert.False(Release(L));
    }

    [Fact]
    public void Releasing_or_adding_a_key_cancels_the_hold()
    {
        _m.HotkeyPressed(0);
        PressChord();
        Release(LAlt);
        _now += 5_000;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);

        Press(LAlt);
        Release(L);
        Press(L);
        Press(VirtualKeys.A);
        _now += 5_000;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);
    }

    [Fact]
    public void Extra_modifiers_do_not_count()
    {
        _m.HotkeyPressed(0);
        Press(LShift);
        PressChord();
        _now += 5_000;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);
    }

    [Fact]
    public void The_hotkey_held_from_locking_must_be_pressed_again()
    {
        _heldAtLock = [VirtualKeys.Control, LCtrl, VirtualKeys.Menu, LAlt, L];
        _m.HotkeyPressed(0);
        Assert.True(Press(L)); // auto-repeat
        _now += 5_000;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);

        Release(L);
        Press(L); // Ctrl and Alt are still physically down from locking
        _now += 3_000;
        _gate.Poll();
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Hold_can_be_turned_off()
    {
        _gate.SetHoldSeconds(0);
        _m.HotkeyPressed(0);
        PressChord();
        _now += 60_000;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);
        Assert.Empty(_holding);
    }

    [Fact]
    public void A_custom_hotkey_is_the_one_that_holds()
    {
        _gate.SetHotkey(new Hotkey(Modifiers.Ctrl | Modifiers.Shift, VirtualKeys.A + ('k' - 'a')));
        _m.HotkeyPressed(0);
        PressChord();
        _now += 5_000;
        _gate.Poll();
        Assert.Equal(LockState.Locked, _m.State);
        Release(L);
        Release(LAlt);
        Release(LCtrl);

        Press(VirtualKeys.RControl);
        Press(VirtualKeys.RShift);
        Press(VirtualKeys.A + ('k' - 'a'));
        _now += 3_000;
        _gate.Poll();
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Hold_does_nothing_in_mouse_only_mode_or_when_unlocked()
    {
        PressChord();
        _now += 5_000;
        _gate.Poll();
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.Empty(_reasons);
    }

    [Fact]
    public void Typing_progress_is_reported_as_it_changes()
    {
        _m.HotkeyPressed(0);
        foreach (char c in "unlz")
            Press(VirtualKeys.A + (c - 'a'));
        Press(VirtualKeys.F1 + 4); // ignored keys don't change progress
        foreach (char c in "unlock")
            Press(VirtualKeys.A + (c - 'a'));
        Assert.Equal([1, 2, 3, 0, 1, 2, 3, 4, 5, 0], _progress);
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Auto_repeat_of_a_key_held_at_lock_time_is_not_typing()
    {
        _gate.SetUnlockWord("llll");
        _heldAtLock = [L];
        _m.HotkeyPressed(0);
        for (int i = 0; i < 10; i++)
            Assert.True(Press(L));
        Assert.Equal(LockState.Locked, _m.State);
        Assert.Empty(_progress);
    }
}

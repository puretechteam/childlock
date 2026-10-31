namespace ChildLock.Tests;

public class HotkeyChordTrackerTests
{
    readonly HotkeyChordTracker _t = new();

    public HotkeyChordTrackerTests() => _t.SetHotkey(Hotkey.Default);

    [Fact]
    public void Left_and_right_modifiers_both_count()
    {
        _t.OnKey(VirtualKeys.RControl, false, 0);
        _t.OnKey(VirtualKeys.LMenu, false, 0);
        Assert.True(_t.OnKey(VirtualKeys.L, false, 0));
    }

    [Fact]
    public void Releasing_one_side_keeps_the_other()
    {
        _t.OnKey(VirtualKeys.LControl, false, 0);
        _t.OnKey(VirtualKeys.RControl, false, 0);
        _t.OnKey(VirtualKeys.LControl, true, 0);
        Assert.Equal(Modifiers.Ctrl, _t.CurrentModifiers());
        _t.OnKey(VirtualKeys.RControl, true, 0);
        Assert.Equal(Modifiers.None, _t.CurrentModifiers());
    }

    [Fact]
    public void Generic_codes_from_the_lock_snapshot_are_released_by_the_sided_key()
    {
        // GetAsyncKeyState reports VK_CONTROL and VK_LCONTROL; the hook only ever releases VK_LCONTROL.
        _t.Reset([VirtualKeys.Control, VirtualKeys.LControl, VirtualKeys.Menu, VirtualKeys.LMenu]);
        Assert.Equal(Modifiers.Ctrl | Modifiers.Alt, _t.CurrentModifiers());
        _t.OnKey(VirtualKeys.LControl, true, 0);
        _t.OnKey(VirtualKeys.LMenu, true, 0);
        Assert.Equal(Modifiers.None, _t.CurrentModifiers());
    }

    [Fact]
    public void Generic_key_up_releases_both_sides()
    {
        _t.OnKey(VirtualKeys.LShift, false, 0);
        _t.OnKey(VirtualKeys.RShift, false, 0);
        _t.OnKey(VirtualKeys.Shift, true, 0);
        Assert.Equal(Modifiers.None, _t.CurrentModifiers());
    }

    [Fact]
    public void Win_counts_as_a_modifier()
    {
        _t.SetHotkey(new Hotkey(Modifiers.Win | Modifiers.Alt, VirtualKeys.A));
        _t.OnKey(VirtualKeys.RWin, false, 0);
        _t.OnKey(VirtualKeys.RMenu, false, 0);
        Assert.True(_t.OnKey(VirtualKeys.A, false, 10));
        Assert.Equal(40, _t.HeldFor(50));
    }
}

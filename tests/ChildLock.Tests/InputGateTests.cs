namespace ChildLock.Tests;

public class InputGateTests
{
    const int LCtrl = 0xA2;
    const int LAlt = 0xA4;
    const int LWin = 0x5B;
    const int Tab = 0x09;
    const int F4 = 0x73;
    const int VolumeUp = 0xAF;
    const int MediaPlayPause = 0xB3;

    readonly LockStateMachine _m = new();
    int[] _heldAtLock = [];
    readonly InputGate _gate;

    public InputGateTests()
    {
        _gate = new InputGate(_m, "unlock", () => _heldAtLock);
    }

    bool Press(int vk) => _gate.OnKey(vk, isUp: false);
    bool Release(int vk) => _gate.OnKey(vk, isUp: true);

    void TypeWord(string word)
    {
        foreach (char c in word)
        {
            Press(VirtualKeys.A + (c - 'a'));
            Release(VirtualKeys.A + (c - 'a'));
        }
    }

    [Fact]
    public void Passes_everything_when_unlocked()
    {
        Assert.False(Press(VirtualKeys.A));
        Assert.False(Release(VirtualKeys.A));
        Assert.False(Press(VirtualKeys.Escape));
        Assert.False(_gate.OnMouse(MouseAction.Move, 0));
        Assert.False(_gate.OnMouse(MouseAction.ButtonDown, VirtualKeys.LButton));
    }

    [Fact]
    public void Blocks_every_key_and_mouse_event_when_locked()
    {
        _m.HotkeyPressed(0);
        int[] keys = [VirtualKeys.A, LWin, Tab, LAlt, F4, VolumeUp, MediaPlayPause, VirtualKeys.Escape, LCtrl];
        foreach (int vk in keys)
        {
            Assert.True(Press(vk), $"down 0x{vk:X2}");
            Assert.True(Release(vk), $"up 0x{vk:X2}");
        }
        Assert.True(_gate.OnMouse(MouseAction.Move, 0));
        Assert.True(_gate.OnMouse(MouseAction.Wheel, 0));
        foreach (int b in new[] { VirtualKeys.LButton, VirtualKeys.RButton, VirtualKeys.MButton, VirtualKeys.XButton1 })
        {
            Assert.True(_gate.OnMouse(MouseAction.ButtonDown, b));
            Assert.True(_gate.OnMouse(MouseAction.ButtonUp, b));
        }
    }

    [Fact]
    public void Releases_of_keys_held_at_lock_time_pass_once()
    {
        _heldAtLock = [LCtrl, LAlt, VirtualKeys.L, VirtualKeys.LButton];
        _m.HotkeyPressed(0);

        Assert.True(Press(LCtrl)); // auto-repeat while held is still blocked
        Assert.False(Release(LCtrl));
        Assert.False(Release(LAlt));
        Assert.False(Release(VirtualKeys.L));
        Assert.False(_gate.OnMouse(MouseAction.ButtonUp, VirtualKeys.LButton));

        // A second press and release is the kid, not us.
        Assert.True(Press(LCtrl));
        Assert.True(Release(LCtrl));
        Assert.True(_gate.OnMouse(MouseAction.ButtonUp, VirtualKeys.LButton));
    }

    [Fact]
    public void Typing_the_word_unlocks_and_input_flows_again()
    {
        _m.HotkeyPressed(0);
        TypeWord("unlo");
        Assert.Equal(LockState.Locked, _m.State);
        Assert.True(Press(VirtualKeys.A + ('c' - 'a')));
        Assert.True(Release(VirtualKeys.A + ('c' - 'a')));
        Assert.True(Press(VirtualKeys.A + ('k' - 'a'))); // completing key is itself blocked
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.False(Release(VirtualKeys.A + ('k' - 'a')));
        Assert.False(Press(VirtualKeys.A));
    }

    [Fact]
    public void Word_progress_does_not_carry_over_between_locks()
    {
        _m.HotkeyPressed(0);
        TypeWord("unlo");
        _m.ForceUnlock(LockReason.SecureDesktop);
        _m.HotkeyPressed(0);
        TypeWord("ck");
        Assert.Equal(LockState.Locked, _m.State);
    }

    [Fact]
    public void Escape_during_countdown_cancels_and_is_swallowed()
    {
        _m.HotkeyPressed(3);
        Assert.False(Press(VirtualKeys.A));
        Assert.False(_gate.OnMouse(MouseAction.Move, 0));
        Assert.True(Press(VirtualKeys.Escape));
        Assert.Equal(LockState.Unlocked, _m.State);
        Assert.False(Release(VirtualKeys.Escape)); // unlocked again, so the release passes
    }

    [Fact]
    public void New_unlock_word_takes_effect()
    {
        _gate.SetUnlockWord("open");
        _m.HotkeyPressed(0);
        TypeWord("unlock");
        Assert.Equal(LockState.Locked, _m.State);
        TypeWord("open");
        Assert.Equal(LockState.Unlocked, _m.State);
    }

    [Fact]
    public void Ten_thousand_random_events_while_locked_are_all_blocked()
    {
        var rng = new Random(7);
        _m.HotkeyPressed(0);
        int passed = 0;
        for (int i = 0; i < 10_000; i++)
        {
            bool blocked;
            if (rng.Next(3) == 0)
            {
                blocked = _gate.OnMouse((MouseAction)rng.Next(4), VirtualKeys.LButton + rng.Next(2));
            }
            else
            {
                int vk = 0x08 + rng.Next(0xF0);
                if (UnlockWordMatcher.ToChar(vk) != '\0') vk = 0x7F; // keep the word itself out of the mix
                blocked = _gate.OnKey(vk, isUp: rng.Next(2) == 0);
            }
            if (!blocked) passed++;
        }
        Assert.Equal(0, passed);
        Assert.Equal(LockState.Locked, _m.State);
    }
}

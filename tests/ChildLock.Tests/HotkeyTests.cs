namespace ChildLock.Tests;

public class HotkeyTests
{
    [Fact]
    public void Default_is_ctrl_alt_l()
    {
        Assert.Equal("Ctrl+Alt+L", Hotkey.Default.ToString());
        Assert.Null(Hotkey.Default.Validate());
    }

    [Theory]
    [InlineData("Ctrl+Alt+L", "Ctrl+Alt+L")]
    [InlineData("alt + ctrl + l", "Ctrl+Alt+L")]
    [InlineData("Control+Shift+F12", "Ctrl+Shift+F12")]
    [InlineData("Win+Alt+Num5", "Alt+Win+Num5")]
    [InlineData("Ctrl+Alt+PageDown", "Ctrl+Alt+PageDown")]
    [InlineData("Ctrl+Alt+Shift+F11", "Ctrl+Alt+Shift+F11")]
    public void Parses_and_formats_canonically(string text, string expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(expected, hotkey.Value.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Ctrl+L")]
    [InlineData("Hyper+L")]
    [InlineData("Ctrl+Alt+Escape")]
    [InlineData("Ctrl+Alt+Delete")]
    public void Rejects_bad_text(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Theory]
    [InlineData("L")]
    [InlineData("Shift+L")]
    public void Requires_ctrl_alt_or_win(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.NotNull(hotkey.Value.Validate());
    }

    [Fact]
    public void Escape_and_modifiers_are_not_supported_keys()
    {
        Assert.False(Hotkey.IsSupportedKey(VirtualKeys.Escape));
        Assert.False(Hotkey.IsSupportedKey(0xA2)); // LCtrl
        Assert.False(Hotkey.IsSupportedKey(0x5B)); // LWin
        Assert.NotNull(new Hotkey(Modifiers.Ctrl, VirtualKeys.Escape).Validate());
    }

    [Fact]
    public void Modifier_values_match_RegisterHotKey()
    {
        Assert.Equal(0x1, (int)Modifiers.Alt);
        Assert.Equal(0x2, (int)Modifiers.Ctrl);
        Assert.Equal(0x4, (int)Modifiers.Shift);
        Assert.Equal(0x8, (int)Modifiers.Win);
    }
}

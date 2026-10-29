using System.ComponentModel;
using ChildLock.Core;
using static ChildLock.Native.NativeMethods;

namespace ChildLock;

/// <summary>A text box that records the key combination you press instead of typing text.</summary>
internal sealed class HotkeyBox : TextBox
{
    Hotkey _value = Hotkey.Default;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        Cursor = Cursors.Arrow;
        Text = _value.ToString();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = value.ToString();
        }
    }

    public event Action? ValueChanged;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        // Selected text reads like "type here"; a plain caret reads like "press keys".
        BeginInvoke(() => Select(TextLength, 0));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Tab && (keyData & Keys.Modifiers) is Keys.None or Keys.Shift)
            return base.ProcessCmdKey(ref msg, keyData);

        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return true;

        var mods = Modifiers.None;
        if (keyData.HasFlag(Keys.Control)) mods |= Modifiers.Ctrl;
        if (keyData.HasFlag(Keys.Alt)) mods |= Modifiers.Alt;
        if (keyData.HasFlag(Keys.Shift)) mods |= Modifiers.Shift;
        if ((GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0)
            mods |= Modifiers.Win;

        var candidate = new Hotkey(mods, (int)key);
        if (Hotkey.IsSupportedKey(candidate.Key))
        {
            Value = candidate;
            ValueChanged?.Invoke();
        }
        return true;
    }
}

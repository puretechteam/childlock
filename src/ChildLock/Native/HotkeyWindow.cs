using ChildLock.Core;
using static ChildLock.Native.NativeMethods;

namespace ChildLock.Native;

/// <summary>
/// Hidden window that owns the global lock hotkey. A second launch of ChildLock finds it by
/// title and asks it to open Settings.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int HotkeyId = 1;
    static readonly int ShowSettingsMessage = RegisterWindowMessage("PureTech.ChildLock.ShowSettings");
    bool _registered;

    public event Action? Pressed;
    public event Action? ShowSettingsRequested;

    public HotkeyWindow(string instanceName)
    {
        CreateHandle(new CreateParams { Caption = Title(instanceName) });
        ChangeWindowMessageFilterEx(Handle, ShowSettingsMessage, MSGFLT_ALLOW, 0);
    }

    static string Title(string instanceName) => "ChildLock " + instanceName;

    /// <summary>Asks the running instance to open Settings. False if none was found.</summary>
    public static bool SignalRunningInstance(string instanceName)
    {
        nint window = FindWindow(null, Title(instanceName));
        return window != 0 && PostMessage(window, ShowSettingsMessage, 0, 0);
    }

    /// <summary>Registers the hotkey, replacing any previous one. False if another app owns it.</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _registered = RegisterHotKey(Handle, HotkeyId, (int)hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId)
            Pressed?.Invoke();
        else if (m.Msg == ShowSettingsMessage)
            ShowSettingsRequested?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}

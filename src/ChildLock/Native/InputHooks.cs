using System.ComponentModel;
using System.Diagnostics;
using ChildLock.Core;
using static ChildLock.Native.NativeMethods;

namespace ChildLock.Native;

/// <summary>Timing for hook callbacks since the last <see cref="Reset"/>.</summary>
internal sealed class HookStats
{
    public long KeyEvents;
    public long MouseEvents;
    public long TotalTicks;
    public long MaxTicks;

    public void Add(long ticks, bool isKey)
    {
        if (isKey) KeyEvents++; else MouseEvents++;
        TotalTicks += ticks;
        if (ticks > MaxTicks) MaxTicks = ticks;
    }

    public void Reset() => KeyEvents = MouseEvents = TotalTicks = MaxTicks = 0;

    public override string ToString()
    {
        long events = KeyEvents + MouseEvents;
        double avgUs = events == 0 ? 0 : TotalTicks * 1e6 / Stopwatch.Frequency / events;
        double maxUs = MaxTicks * 1e6 / Stopwatch.Frequency;
        return $"keys={KeyEvents} mouse={MouseEvents} avg_us={avgUs:F2} max_us={maxUs:F2}";
    }
}

/// <summary>
/// Global low-level keyboard and mouse hooks. Callbacks run on the thread that installed them
/// (the UI thread) and must return quickly, or Windows silently removes the hook.
/// </summary>
internal sealed class InputHooks : IDisposable
{
    readonly InputGate _gate;
    readonly HookProc _keyboardProc;
    readonly HookProc _mouseProc;
    readonly nint _module = GetModuleHandle(null);
    nint _keyboardHook;
    nint _mouseHook;

    public HookStats Stats { get; } = new();

    public InputHooks(InputGate gate)
    {
        _gate = gate;
        // Keep the delegates in fields so the GC never collects them while Windows holds pointers.
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public bool Installed => _keyboardHook != 0;

    public void Install()
    {
        if (Installed) return;
        _keyboardHook = Set(WH_KEYBOARD_LL, _keyboardProc);
        _mouseHook = Set(WH_MOUSE_LL, _mouseProc);
    }

    /// <summary>
    /// Installs fresh hooks before removing the old ones, so there is no gap. Recovers from Windows
    /// dropping a hook after a timeout, which it does without telling the app.
    /// </summary>
    public void Reinstall()
    {
        if (!Installed) return;
        var keyboard = Set(WH_KEYBOARD_LL, _keyboardProc);
        var mouse = Set(WH_MOUSE_LL, _mouseProc);
        UnhookWindowsHookEx(_keyboardHook);
        UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = keyboard;
        _mouseHook = mouse;
    }

    public void Uninstall()
    {
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = 0;
    }

    public void Dispose() => Uninstall();

    nint Set(int id, HookProc proc)
    {
        var hook = SetWindowsHookEx(id, proc, _module, 0);
        if (hook == 0) throw new Win32Exception();
        return hook;
    }

    unsafe nint KeyboardCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            long start = Stopwatch.GetTimestamp();
            var info = (KBDLLHOOKSTRUCT*)lParam;
            bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;
            bool block = _gate.OnKey((int)info->vkCode, isUp);
            Stats.Add(Stopwatch.GetTimestamp() - start, isKey: true);
            if (block) return 1;
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    unsafe nint MouseCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            long start = Stopwatch.GetTimestamp();
            var info = (MSLLHOOKSTRUCT*)lParam;
            int xButton = (int)(info->mouseData >> 16) == 1 ? Core.VirtualKeys.XButton1 : Core.VirtualKeys.XButton2;
            (MouseAction action, int button) = (int)wParam switch
            {
                WM_MOUSEMOVE => (MouseAction.Move, 0),
                WM_LBUTTONDOWN => (MouseAction.ButtonDown, Core.VirtualKeys.LButton),
                WM_LBUTTONUP => (MouseAction.ButtonUp, Core.VirtualKeys.LButton),
                WM_RBUTTONDOWN => (MouseAction.ButtonDown, Core.VirtualKeys.RButton),
                WM_RBUTTONUP => (MouseAction.ButtonUp, Core.VirtualKeys.RButton),
                WM_MBUTTONDOWN => (MouseAction.ButtonDown, Core.VirtualKeys.MButton),
                WM_MBUTTONUP => (MouseAction.ButtonUp, Core.VirtualKeys.MButton),
                WM_XBUTTONDOWN => (MouseAction.ButtonDown, xButton),
                WM_XBUTTONUP => (MouseAction.ButtonUp, xButton),
                _ => (MouseAction.Wheel, 0), // WM_MOUSEWHEEL, WM_MOUSEHWHEEL and anything new
            };
            bool block = _gate.OnMouse(action, button);
            Stats.Add(Stopwatch.GetTimestamp() - start, isKey: false);
            if (block) return 1;
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }
}

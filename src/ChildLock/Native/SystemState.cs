using static ChildLock.Native.NativeMethods;

namespace ChildLock.Native;

/// <summary>Small wrappers over Windows state the lock needs to read or set.</summary>
internal static class SystemState
{
    /// <summary>Keeps the display and system awake while locked; pass false to release.</summary>
    public static void KeepAwake(bool on) =>
        SetThreadExecutionState(on
            ? ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED
            : ES_CONTINUOUS);

    /// <summary>Milliseconds since the last keyboard or mouse input anywhere in the session.</summary>
    public static uint IdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    /// <summary>
    /// Virtual keys and mouse buttons currently held down. Mouse buttons are reported as the
    /// logical button (what the hook sees), so swapped buttons for left-handed users still match.
    /// </summary>
    public static List<int> HeldInput()
    {
        var held = new List<int>();
        bool swapped = SystemInformation.MouseButtonsSwapped;
        for (int vk = 1; vk < 255; vk++)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            held.Add(swapped && vk is Core.VirtualKeys.LButton or Core.VirtualKeys.RButton
                ? (vk == Core.VirtualKeys.LButton ? Core.VirtualKeys.RButton : Core.VirtualKeys.LButton)
                : vk);
        }
        return held;
    }

    /// <summary>
    /// True when the normal desktop has input. False while the Ctrl+Alt+Del screen (or another
    /// secure desktop) is showing, because only SYSTEM can open those.
    /// </summary>
    public static bool IsDefaultDesktopActive()
    {
        var desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (desktop == 0) return false;
        try
        {
            var name = new char[64];
            if (!GetUserObjectInformation(desktop, UOI_NAME, name, name.Length * 2, out _)) return false;
            return new string(name).TrimEnd('\0').Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }
}

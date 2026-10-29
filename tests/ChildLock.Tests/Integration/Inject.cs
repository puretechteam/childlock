using System.Runtime.InteropServices;

namespace ChildLock.Tests.Integration;

/// <summary>Synthesizes keyboard and mouse input with SendInput, like a real device would.</summary>
internal static class Inject
{
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_LWIN = 0x5B;
    public const int VK_F11 = 0x7A;
    public const int VK_F13 = 0x7C;

    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    public static void KeyDown(int vk) => Send(Key(vk, 0));
    public static void KeyUp(int vk) => Send(Key(vk, KEYEVENTF_KEYUP));

    public static void Tap(int vk)
    {
        KeyDown(vk);
        KeyUp(vk);
    }

    public static void Chord(params int[] vks)
    {
        foreach (var vk in vks) KeyDown(vk);
        foreach (var vk in vks.Reverse()) KeyUp(vk);
    }

    public static void Type(string letters)
    {
        foreach (char c in letters.ToUpperInvariant()) Tap(c);
    }

    public static void MoveBy(int dx, int dy) => Send(Mouse(MOUSEEVENTF_MOVE, dx, dy, 0));
    public static void Wheel(int delta) => Send(Mouse(MOUSEEVENTF_WHEEL, 0, 0, delta));
    public static void LeftClick() { Send(Mouse(MOUSEEVENTF_LEFTDOWN, 0, 0, 0)); Send(Mouse(MOUSEEVENTF_LEFTUP, 0, 0, 0)); }
    public static void RightClick() { Send(Mouse(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0)); Send(Mouse(MOUSEEVENTF_RIGHTUP, 0, 0, 0)); }

    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    static INPUT Key(int vk, uint flags) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = flags } },
    };

    static INPUT Mouse(uint flags, int dx, int dy, int data) => new()
    {
        type = 0,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
    };

    static void Send(INPUT input)
    {
        var inputs = new[] { input };
        if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1)
            throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
        Thread.Sleep(4);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
}

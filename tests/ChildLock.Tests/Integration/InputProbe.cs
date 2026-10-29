using System.Runtime.InteropServices;

namespace ChildLock.Tests.Integration;

/// <summary>
/// Low-level hooks installed by the test before ChildLock locks. Windows calls the newest hook
/// first, so when ChildLock blocks an event this probe never sees it. Its counters therefore
/// measure exactly what leaked past the lock.
/// </summary>
internal sealed class InputProbe : IDisposable
{
    readonly Thread _thread;
    readonly ManualResetEventSlim _ready = new();
    uint _threadId;
    nint _focusWindow;
    HookProc? _keyboardProc;
    HookProc? _mouseProc;
    int _keys;
    int _mouse;

    public int Keys => Volatile.Read(ref _keys);
    public int Mouse => Volatile.Read(ref _mouse);

    public InputProbe()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "InputProbe" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Probe hooks did not install.");
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _keys, 0);
        Interlocked.Exchange(ref _mouse, 0);
    }

    /// <summary>
    /// Brings the probe's own 1x1 window to the front and proves injected input arrives.
    /// Windows silently drops SendInput while an elevated window has focus, and with our own
    /// window in front, any key that ever leaked past a lock would land here, not in your apps.
    /// </summary>
    public void Preflight()
    {
        SetForegroundWindow(_focusWindow);
        Thread.Sleep(50);
        Reset();
        Inject.Tap(Inject.VK_F13);
        Thread.Sleep(100);
        if (Keys < 2)
        {
            throw new InvalidOperationException(
                $"Injected input is not arriving (seen {Keys}/2). The focused window probably belongs to an " +
                "elevated app, and Windows drops synthesized input aimed at it. Click a normal window and run again.");
        }
        Reset();
    }

    void Run()
    {
        _threadId = GetCurrentThreadId();
        _focusWindow = CreateWindowEx(0x80 /* WS_EX_TOOLWINDOW */ | 0x8 /* WS_EX_TOPMOST */, "STATIC", "ChildLock test focus",
            unchecked((int)0x90000000) /* WS_POPUP | WS_VISIBLE */, 0, 0, 1, 1, 0, 0, 0, 0);
        _keyboardProc = (code, w, l) => { if (code >= 0) Interlocked.Increment(ref _keys); return CallNextHookEx(0, code, w, l); };
        _mouseProc = (code, w, l) => { if (code >= 0) Interlocked.Increment(ref _mouse); return CallNextHookEx(0, code, w, l); };
        nint module = GetModuleHandle(null);
        nint kb = SetWindowsHookEx(13, _keyboardProc, module, 0);
        nint ms = SetWindowsHookEx(14, _mouseProc, module, 0);
        _ready.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(kb);
        UnhookWindowsHookEx(ms);
        DestroyWindow(_focusWindow);
    }

    public void Dispose()
    {
        PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
}

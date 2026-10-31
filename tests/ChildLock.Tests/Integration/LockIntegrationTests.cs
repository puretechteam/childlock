using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace ChildLock.Tests.Integration;

/// <summary>
/// Drives the real exe with synthesized input. These briefly lock this PC's keyboard and mouse
/// (a few seconds each), so they live in one class and never run in parallel with each other.
/// Skip them with: dotnet test --filter Category!=Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class LockIntegrationTests(ITestOutputHelper output)
{
    // Function keys F13-F24 do nothing in almost every app, so a failed lock can't type into anything.
    static readonly int[] HarmlessKeys = [.. Enumerable.Range(Inject.VK_F13, 12)];

    [Fact]
    public void Locks_blocks_everything_then_unlocks_with_the_word()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 0);
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Locked reason=Hotkey");
        Thread.Sleep(200);
        probe.Reset();
        var cursor = Inject.CursorPosition();

        // Phase 1: harmless keys and mouse movement.
        var rng = new Random(40);
        for (int i = 0; i < 40; i++) Inject.Tap(HarmlessKeys[rng.Next(HarmlessKeys.Length)]);
        for (int i = 0; i < 20; i++) Inject.MoveBy(15, 10);
        Thread.Sleep(200);
        output.WriteLine($"phase 1 leaked: keys={probe.Keys} mouse={probe.Mouse}, cursor moved={Inject.CursorPosition() != cursor}");
        Assert.Equal(0, probe.Keys);
        Assert.Equal(0, probe.Mouse);
        Assert.Equal(cursor, Inject.CursorPosition());

        // Phase 2: only now that blocking is proven, the riskier input: letters, Win, Alt+Tab, Alt+F4, clicks, wheel.
        Inject.Type("qwertyasdfzxcv");
        Inject.Tap(Inject.VK_LWIN);
        Inject.Chord(Inject.VK_MENU, 0x09);
        Inject.Chord(Inject.VK_MENU, 0x73);
        Inject.Chord(Inject.VK_CONTROL, Inject.VK_ESCAPE);
        Inject.Tap(0xAF); // volume up
        Inject.Tap(0xB3); // play/pause
        Inject.LeftClick();
        Inject.RightClick();
        Inject.Wheel(-120);
        Thread.Sleep(200);
        output.WriteLine($"phase 2 leaked: keys={probe.Keys} mouse={probe.Mouse}");
        Assert.Equal(0, probe.Keys);
        Assert.Equal(0, probe.Mouse);
        Assert.Equal(cursor, Inject.CursorPosition());

        // Unlock by typing the word; input must flow again afterwards.
        Inject.Type("unlock");
        app.WaitFor("state=Unlocked reason=UnlockWord");
        string session = app.WaitFor("lock session");
        Thread.Sleep(200);
        probe.Reset();
        Inject.Tap(Inject.VK_F13);
        Inject.MoveBy(1, 0);
        Inject.MoveBy(-1, 0);
        Thread.Sleep(200);
        output.WriteLine($"after unlock passed: keys={probe.Keys} mouse={probe.Mouse}");
        Assert.Equal(2, probe.Keys);
        Assert.True(probe.Mouse >= 2);

        // Nothing is left "stuck" down by the lock.
        Assert.False(Inject.IsDown(Inject.VK_CONTROL));
        Assert.False(Inject.IsDown(Inject.VK_MENU));
        Assert.False(Inject.IsDown(Inject.VK_SHIFT));
        Assert.False(Inject.IsDown(0x01));

        output.WriteLine("hook timing: " + session);
        var match = MaxUs().Match(session);
        Assert.True(match.Success);
        Assert.True(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < 1000,
            "a hook callback took 1 ms or more");
    }

    [Fact]
    public void Blocking_survives_the_periodic_hook_reinstall()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 0);
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Locked");
        app.WaitFor("rehooked", timeoutMs: 8000);
        app.WaitFor("rehooked", timeoutMs: 8000);
        Thread.Sleep(100);
        probe.Reset();
        foreach (int vk in HarmlessKeys) Inject.Tap(vk);
        Inject.MoveBy(10, 10);
        Thread.Sleep(200);
        output.WriteLine($"after 2 reinstalls leaked: keys={probe.Keys} mouse={probe.Mouse}");
        Assert.Equal(0, probe.Keys);
        Assert.Equal(0, probe.Mouse);

        Inject.Type("unlock");
        app.WaitFor("state=Unlocked reason=UnlockWord");
    }

    [Fact]
    public void Escape_cancels_the_countdown_and_is_swallowed()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 3);
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=CountingDown reason=Hotkey");
        Thread.Sleep(100);
        probe.Reset();
        Inject.Tap(HarmlessKeys[0]);
        Inject.MoveBy(1, 0);
        Inject.MoveBy(-1, 0);
        Thread.Sleep(100);
        Assert.Equal(2, probe.Keys); // countdown does not block normal input yet
        Assert.True(probe.Mouse >= 2);

        probe.Reset();
        Inject.KeyDown(Inject.VK_ESCAPE);
        app.WaitFor("state=Unlocked reason=Cancelled");
        Inject.KeyUp(Inject.VK_ESCAPE);
        Thread.Sleep(100);
        Assert.Equal(1, probe.Keys); // only the Esc release, after the cancel
        Assert.DoesNotContain("state=Locked", app.ReadLog());
    }

    [Fact]
    public void Hotkey_again_cancels_the_countdown()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 3);
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=CountingDown reason=Hotkey");
        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Unlocked reason=Cancelled");
        Thread.Sleep(3500);
        Assert.DoesNotContain("state=Locked", app.ReadLog());
    }

    [Fact]
    public void Countdown_locks_after_the_set_time()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 2);
        probe.Preflight();

        var sw = Stopwatch.StartNew();
        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=CountingDown");
        app.WaitFor("state=Locked reason=CountdownFinished", timeoutMs: 5000);
        long ms = sw.ElapsedMilliseconds;
        output.WriteLine($"countdown 2 s locked after {ms} ms");
        Assert.InRange(ms, 1800, 2800);

        Inject.Type("unlock");
        app.WaitFor("state=Unlocked reason=UnlockWord");
    }

    [Fact]
    public void Footprint_idle_and_locked()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 0);
        probe.Preflight();
        Thread.Sleep(1500);

        var cpu0 = app.CpuTime;
        var sw = Stopwatch.StartNew();
        Thread.Sleep(10_000);
        double idleCpuPct = (app.CpuTime - cpu0).TotalMilliseconds / sw.ElapsedMilliseconds / Environment.ProcessorCount * 100;
        long idleWs = app.WorkingSetBytes;

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Locked");
        Thread.Sleep(500);
        cpu0 = app.CpuTime;
        sw.Restart();
        Thread.Sleep(10_000);
        double lockedCpuPct = (app.CpuTime - cpu0).TotalMilliseconds / sw.ElapsedMilliseconds / Environment.ProcessorCount * 100;
        long lockedWs = app.WorkingSetBytes;
        Inject.Type("unlock");
        app.WaitFor("state=Unlocked");

        output.WriteLine($"idle:   cpu={idleCpuPct:F3}% working_set={idleWs / 1048576.0:F1} MB");
        output.WriteLine($"locked: cpu={lockedCpuPct:F3}% working_set={lockedWs / 1048576.0:F1} MB");
        Assert.True(idleCpuPct < 1);
        Assert.True(lockedCpuPct < 1);
    }

    [Fact]
    public void Holding_the_hotkey_unlocks_and_leaves_nothing_stuck()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 0, holdSeconds: 2);
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Locked reason=Hotkey");
        Thread.Sleep(200);
        probe.Reset();

        // Injected keys don't auto-repeat, so this also proves the timer completes the hold on its own.
        foreach (int vk in AppHarness.HotkeyVks) Inject.KeyDown(vk);
        app.WaitFor("state=Unlocked reason=HoldHotkey", timeoutMs: 6000);
        foreach (int vk in AppHarness.HotkeyVks.Reverse()) Inject.KeyUp(vk);
        Thread.Sleep(200);

        output.WriteLine($"hold leaked: keys={probe.Keys} mouse={probe.Mouse}");
        Assert.Equal(0, probe.Keys);
        Assert.False(Inject.IsDown(Inject.VK_CONTROL));
        Assert.False(Inject.IsDown(Inject.VK_MENU));
        Assert.False(Inject.IsDown(Inject.VK_SHIFT));
    }

    [Fact]
    public void Keyboard_only_lock_leaves_the_mouse_working()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 0, lockMode: "keyboardOnly");
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Locked reason=Hotkey mode=KeyboardOnly");
        Thread.Sleep(200);
        probe.Reset();
        var cursor = Inject.CursorPosition();
        foreach (int vk in HarmlessKeys) Inject.Tap(vk);
        Inject.MoveBy(5, 5);
        Thread.Sleep(200);
        output.WriteLine($"keyboard only: keys={probe.Keys} mouse={probe.Mouse}");
        Assert.Equal(0, probe.Keys);
        Assert.True(probe.Mouse >= 1);
        Assert.NotEqual(cursor, Inject.CursorPosition());
        Inject.MoveBy(-5, -5);

        Inject.Type("unlock");
        app.WaitFor("state=Unlocked reason=UnlockWord");
    }

    [Fact]
    public void Mouse_only_lock_leaves_keys_working_and_the_hotkey_unlocks()
    {
        using var probe = new InputProbe();
        using var app = new AppHarness(countdownSeconds: 0, lockMode: "mouseOnly");
        probe.Preflight();

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Locked reason=Hotkey mode=MouseOnly");
        Thread.Sleep(200);
        probe.Reset();
        var cursor = Inject.CursorPosition();
        Inject.Tap(Inject.VK_F13);
        Inject.MoveBy(10, 10);
        Inject.LeftClick();
        Thread.Sleep(200);
        output.WriteLine($"mouse only: keys={probe.Keys} mouse={probe.Mouse}");
        Assert.Equal(2, probe.Keys);
        Assert.Equal(0, probe.Mouse);
        Assert.Equal(cursor, Inject.CursorPosition());

        Inject.Chord(AppHarness.HotkeyVks);
        app.WaitFor("state=Unlocked reason=Hotkey");
        Thread.Sleep(500);
        probe.Reset();
        Inject.MoveBy(1, 0);
        Inject.MoveBy(-1, 0);
        Thread.Sleep(200);
        Assert.True(probe.Mouse >= 2); // unlocked, and the same press didn't start a new lock
    }

    [GeneratedRegex(@"max_us=([\d.]+)")]
    private static partial Regex MaxUs();
}

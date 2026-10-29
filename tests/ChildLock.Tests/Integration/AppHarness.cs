using System.Diagnostics;

namespace ChildLock.Tests.Integration;

/// <summary>
/// Runs the real ChildLock.exe against a throwaway settings folder with --diag, and reads the
/// diag log to follow its state. Always kills the process on dispose, which also removes its hooks.
/// </summary>
internal sealed class AppHarness : IDisposable
{
    public const string Hotkey = "Ctrl+Alt+Shift+F11";
    public static readonly int[] HotkeyVks = [Inject.VK_CONTROL, Inject.VK_MENU, Inject.VK_SHIFT, Inject.VK_F11];

    readonly string _dir = Path.Combine(Path.GetTempPath(), "childlock-it-" + Guid.NewGuid().ToString("N"));
    readonly Process _process;
    int _readOffset;

    public string LogPath => Path.Combine(_dir, "diag.log");

    public AppHarness(int countdownSeconds, string unlockWord = "unlock")
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), $$"""
            {
              "lockHotkey": "{{Hotkey}}",
              "unlockWord": "{{unlockWord}}",
              "countdownSeconds": {{countdownSeconds}},
              "idleLockMinutes": 0,
              "soundEnabled": false,
              "firstRunDone": true
            }
            """);

        // Redirected so the app doesn't inherit (and hold open) the test runner's output pipes.
        var info = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--settings");
        info.ArgumentList.Add(Path.Combine(_dir, "settings.json"));
        info.ArgumentList.Add("--diag");
        _process = Process.Start(info)!;

        try
        {
            string started = WaitFor(" started settings=");
            if (!started.Contains("registered=True"))
                throw new InvalidOperationException("ChildLock could not register the test hotkey: " + started);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static string ExePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChildLock.slnx"))) dir = dir.Parent;
            if (dir is null) throw new DirectoryNotFoundException("Repo root not found.");
            // tests/ChildLock.Tests/bin/<Configuration>/<tfm>/
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var bin = Path.Combine(dir.FullName, "src", "ChildLock", "bin", configuration);
            return Directory.EnumerateFiles(bin, "ChildLock.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
        }
    }

    /// <summary>Waits for the next log line (after the last one matched) that contains <paramref name="text"/>.</summary>
    public string WaitFor(string text, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (_process.HasExited) throw new InvalidOperationException($"ChildLock exited with {_process.ExitCode}. Log:\n{ReadLog()}");
            string log = ReadLog();
            int at = log.IndexOf(text, _readOffset, StringComparison.Ordinal);
            if (at >= 0)
            {
                int end = log.IndexOf('\n', at);
                if (end < 0) end = log.Length;
                int start = log.LastIndexOf('\n', at) + 1;
                _readOffset = end;
                return log[start..end].TrimEnd();
            }
            Thread.Sleep(20);
        }
        throw new TimeoutException($"'{text}' not logged within {timeoutMs} ms. Log:\n{ReadLog()}");
    }

    public string ReadLog()
    {
        if (!File.Exists(LogPath)) return "";
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    public long WorkingSetBytes
    {
        get
        {
            _process.Refresh();
            return _process.WorkingSet64;
        }
    }

    public TimeSpan CpuTime
    {
        get
        {
            _process.Refresh();
            return _process.TotalProcessorTime;
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        finally
        {
            _process.Dispose();
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }
    }
}

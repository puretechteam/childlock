using System.Diagnostics;
using ChildLock.Core;

namespace ChildLock;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var options = AppOptions.Parse(args);

        if (options.ExportIconPath is { } iconPath)
        {
            IconFactory.WriteIco(iconPath);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(options.Theme);

        if (options.Uninstall) return Uninstall.Run(options);

        // A restart with admin rights waits for the copy it replaces, so the two never hold the hotkey together.
        if (options.ReplacePid is { } pid) WaitForExit(pid);

        using var mutex = new Mutex(initiallyOwned: true, options.MutexName, out bool firstInstance);
        if (!firstInstance) return SignalRunningInstance(options);

        bool declined = false;
        if (Elevation.SelfElevateEnabled && !options.NoElevate && !Elevation.IsElevated
            && SettingsStore.Load(options.SettingsPath).RunAsAdmin)
        {
            // Let the elevated copy take over the instance mutex; --no-elevate stops a loop if Windows
            // starts it without admin rights after all (UAC turned off for a standard account).
            mutex.ReleaseMutex();
            switch (Elevation.Relaunch([.. options.PassThrough, "--no-elevate"]))
            {
                case RelaunchResult.Started:
                    return 0;
                case RelaunchResult.Declined:
                    declined = true;
                    RememberDecline(options.SettingsPath);
                    break;
            }
            if (!mutex.WaitOne(0)) return SignalRunningInstance(options);
        }

        if (options.DiagPath is { } diagPath) Diag.Enable(diagPath);

        // If anything goes wrong, log it and keep running. Should the process ever die,
        // Windows removes its hooks, so input always comes back.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Diag.Log("error: " + e.Exception);

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayContext(options, elevationDeclined: declined));
        return 0;
    }

    static int SignalRunningInstance(AppOptions options)
    {
        // Launching it again opens Settings in the copy that's already running.
        if (Native.HotkeyWindow.SignalRunningInstance(options.MutexName)) return 0;
        MessageBox.Show("ChildLock is already running. Look for the padlock in the notification area.",
            "ChildLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 1;
    }

    /// <summary>You said no to the admin prompt: stop asking. "Run as administrator" in Settings turns it back on.</summary>
    static void RememberDecline(string settingsPath)
    {
        try
        {
            SettingsStore.Save(settingsPath, SettingsStore.Load(settingsPath) with { RunAsAdmin = false });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Asking again next time is the safe fallback.
        }
    }

    static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}

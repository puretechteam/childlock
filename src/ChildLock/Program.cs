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

        using var mutex = new Mutex(initiallyOwned: true, options.MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            // Launching it again opens Settings in the copy that's already running.
            if (Native.HotkeyWindow.SignalRunningInstance(options.MutexName)) return 0;
            MessageBox.Show("ChildLock is already running. Look for the padlock in the notification area.",
                "ChildLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        if (options.DiagPath is { } diagPath) Diag.Enable(diagPath);

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(options.Theme);

        // If anything goes wrong, log it and keep running. Should the process ever die,
        // Windows removes its hooks, so input always comes back.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Diag.Log("error: " + e.Exception);

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new TrayContext(options));
        return 0;
    }
}

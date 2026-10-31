using PureTech.Tray;

namespace ChildLock;

/// <summary>
/// <c>ChildLock.exe --uninstall</c>: removes what ChildLock added to Windows (the sign-in entry, the toast
/// registration with its Start menu shortcut, the cached icon) and offers to delete the settings.
/// </summary>
internal static class Uninstall
{
    public static int Run(AppOptions options)
    {
        if (Mutex.TryOpenExisting(options.MutexName, out var running))
        {
            running.Dispose();
            MessageBox.Show("ChildLock is still running. Right-click the padlock, choose Exit, then run the uninstall again.",
                "Uninstall ChildLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        var problems = new List<string>();
        if (StartupTask.Disable() is { } startupError) problems.Add(startupError);
        try
        {
            ToastRegistration.Unregister(TrayIcon.AppId);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            problems.Add("Couldn't remove the Start menu entry: " + e.Message);
        }
        TryDeleteDirectory(IconFactory.LocalDataDirectory, problems);

        string settingsDir = Path.GetDirectoryName(options.SettingsPath)!;
        if (Directory.Exists(settingsDir) &&
            MessageBox.Show($"Also delete your ChildLock settings?\n\n{settingsDir}", "Uninstall ChildLock",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            if (options.CustomSettingsPath) TryDelete(options.SettingsPath, problems);
            else TryDeleteDirectory(settingsDir, problems);
        }

        string message = problems.Count == 0
            ? "ChildLock no longer starts at sign-in and its Start menu entry is gone. You can delete ChildLock.exe now."
            : "Mostly done, but:\n\n" + string.Join("\n", problems);
        MessageBox.Show(message, "Uninstall ChildLock", MessageBoxButtons.OK,
            problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return problems.Count == 0 ? 0 : 1;
    }

    static void TryDelete(string path, List<string> problems)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add(e.Message);
        }
    }

    static void TryDeleteDirectory(string path, List<string> problems)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add(e.Message);
        }
    }
}

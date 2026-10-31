using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using ChildLock.Core;
using Microsoft.Win32;

namespace ChildLock;

/// <summary>
/// Starts ChildLock at sign-in. With admin rights this is a Task Scheduler task that starts it elevated
/// without a prompt; without them it's the normal per-user Run entry.
/// </summary>
internal static class StartupTask
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "ChildLock";

    static string Schtasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    /// <summary>The program path started at sign-in, or null if nothing is set up.</summary>
    public static string? RegisteredCommand() => TaskCommand() ?? RunKeyCommand();

    /// <summary>Returns null on success, otherwise an error to show.</summary>
    public static string? Enable(string exePath)
    {
        if (!Elevation.IsElevated)
            return SetRunKey(exePath);
        string? error = EnableTask(exePath);
        if (error is null) RemoveRunKey();
        return error;
    }

    public static string? Disable()
    {
        RemoveRunKey();
        if (TaskCommand() is null) return null;
        var (code, output) = Run("/Delete", "/TN", StartupTaskXml.TaskName, "/F");
        if (code == 0) return null;
        return Elevation.IsElevated
            ? "Couldn't remove the sign-in task: " + output.Trim()
            : "The sign-in task was set up with admin rights, so removing it needs them too. Turn on \"Run as administrator\", or remove it from an admin prompt: schtasks /Delete /TN \"PureTech\\ChildLock\" /F";
    }

    /// <summary>If the exe moved, points sign-in at the new place. With admin rights, upgrades a Run entry to the task.</summary>
    public static void Repair(string current)
    {
        string? task = TaskCommand();
        string? run = RunKeyCommand();
        if (task is not null && !SamePath(task, current) && Elevation.IsElevated)
            Diag.Log($"startup task repointed error={EnableTask(current) ?? "none"}");
        if (run is not null && !Elevation.IsElevated && !SamePath(run, current))
            Diag.Log($"run key repointed error={SetRunKey(current) ?? "none"}");
        if (run is not null && Elevation.IsElevated)
            Diag.Log($"run key upgraded to task error={Enable(current) ?? "none"}");
    }

    static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string? TaskCommand()
    {
        var (code, output) = Run("/Query", "/TN", StartupTaskXml.TaskName, "/XML");
        return code == 0 ? StartupTaskXml.ReadCommand(output) : null;
    }

    static string? EnableTask(string exePath)
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), $"childlock-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, StartupTaskXml.Build(exePath, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
            var (code, output) = Run("/Create", "/TN", StartupTaskXml.TaskName, "/XML", xmlPath, "/F");
            return code == 0 ? null : "Couldn't create the sign-in task: " + output.Trim();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    static string? RunKeyCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string value ? StartupTaskXml.ReadRunCommand(value) : null;
    }

    static string? SetRunKey(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(RunValueName, $"\"{exePath}\"");
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return "Couldn't add ChildLock to sign-in: " + e.Message;
        }
    }

    static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Diag.Log("couldn't remove run key: " + e.Message);
        }
    }

    static (int Code, string Output) Run(params string[] args)
    {
        var info = new ProcessStartInfo(Schtasks)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, process.ExitCode == 0 ? stdout : stderr.Result + stdout);
    }
}

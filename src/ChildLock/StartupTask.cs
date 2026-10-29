using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using ChildLock.Core;

namespace ChildLock;

/// <summary>Creates or removes the elevated sign-in task through schtasks.exe.</summary>
internal static class StartupTask
{
    static string Schtasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    /// <summary>The program path the task starts, or null if there is no task.</summary>
    public static string? RegisteredCommand()
    {
        var (code, output) = Run("/Query", "/TN", StartupTaskXml.TaskName, "/XML");
        return code == 0 ? StartupTaskXml.ReadCommand(output) : null;
    }

    /// <summary>Returns null on success, otherwise an error to show.</summary>
    public static string? Enable(string exePath)
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

    public static string? Disable()
    {
        if (RegisteredCommand() is null) return null;
        var (code, output) = Run("/Delete", "/TN", StartupTaskXml.TaskName, "/F");
        return code == 0 ? null : "Couldn't remove the sign-in task: " + output.Trim();
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

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace ChildLock;

internal enum RelaunchResult
{
    Started,
    Declined,
    Failed,
}

/// <summary>Admin rights let the hooks also block input aimed at admin windows such as Task Manager.</summary>
internal static class Elevation
{
    const int ErrorCancelled = 1223;

    /// <summary>Release builds ask Windows for admin rights at start; Debug builds never do.</summary>
    // Not a const, so code behind it doesn't count as unreachable in Debug builds.
    public static readonly bool SelfElevateEnabled =
#if CHILDLOCK_SELF_ELEVATE
        true;
#else
        false;
#endif

    public static readonly bool IsElevated = CheckElevated();

    static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Starts this exe again with admin rights (the UAC prompt).</summary>
    public static RelaunchResult Relaunch(IEnumerable<string> args)
    {
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = JoinArguments(args),
                WorkingDirectory = AppContext.BaseDirectory,
            };
            using var process = Process.Start(info);
            return process is null ? RelaunchResult.Failed : RelaunchResult.Started;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return RelaunchResult.Declined;
        }
        catch (Win32Exception e)
        {
            Diag.Log("elevated relaunch failed: " + e.Message);
            return RelaunchResult.Failed;
        }
    }

    /// <summary>Quotes arguments the way the C runtime parses them back.</summary>
    public static string JoinArguments(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (string arg in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            {
                sb.Append(arg);
                continue;
            }
            sb.Append('"');
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
        }
        return sb.ToString();
    }
}

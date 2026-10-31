using System.Xml.Linq;

namespace ChildLock.Core;

/// <summary>
/// Builds the Task Scheduler definition that starts ChildLock elevated at sign-in.
/// Unlike a plain "schtasks /SC ONLOGON", it removes the default 72-hour time limit,
/// keeps running on battery, and uses normal priority so the input hooks stay responsive.
/// </summary>
public static class StartupTaskXml
{
    public const string TaskName = @"PureTech\ChildLock";

    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string Build(string exePath, string userId)
    {
        XElement E(string name, params object[] content) => new(Ns + name, content);

        var doc = new XDocument(
            E("Task", new XAttribute("version", "1.2"),
                E("RegistrationInfo",
                    E("Author", "Pure Tech"),
                    E("Description", "Starts ChildLock when you sign in.")),
                E("Triggers",
                    E("LogonTrigger",
                        E("Enabled", "true"),
                        E("UserId", userId))),
                E("Principals",
                    E("Principal", new XAttribute("id", "Author"),
                        E("UserId", userId),
                        E("LogonType", "InteractiveToken"),
                        E("RunLevel", "HighestAvailable"))),
                E("Settings",
                    E("MultipleInstancesPolicy", "IgnoreNew"),
                    E("DisallowStartIfOnBatteries", "false"),
                    E("StopIfGoingOnBatteries", "false"),
                    E("AllowHardTerminate", "true"),
                    E("StartWhenAvailable", "false"),
                    E("RunOnlyIfNetworkAvailable", "false"),
                    E("IdleSettings",
                        E("StopOnIdleEnd", "false"),
                        E("RestartOnIdle", "false")),
                    E("AllowStartOnDemand", "true"),
                    E("Enabled", "true"),
                    E("Hidden", "false"),
                    E("RunOnlyIfIdle", "false"),
                    E("WakeToRun", "false"),
                    E("ExecutionTimeLimit", "PT0S"),
                    E("Priority", "4")),
                E("Actions", new XAttribute("Context", "Author"),
                    E("Exec",
                        E("Command", exePath)))));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + doc;
    }

    /// <summary>Reads the program path out of a task definition, or null.</summary>
    public static string? ReadCommand(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants(Ns + "Command").FirstOrDefault()?.Value.Trim('"');
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The program path out of a Run entry such as <c>"C:\Tools\ChildLock.exe" --diag</c>, or null.</summary>
    public static string? ReadRunCommand(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        if (value[0] == '"')
        {
            int end = value.IndexOf('"', 1);
            return end > 1 ? value[1..end] : null;
        }
        int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? value[..(exe + 4)] : value;
    }
}

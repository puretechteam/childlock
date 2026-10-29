using System.Xml.Linq;

namespace ChildLock.Tests;

public class StartupTaskXmlTests
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    const string Exe = @"C:\Tools\Child Lock\ChildLock.exe";
    const string User = @"PC\tester";

    static XDocument Doc() => XDocument.Parse(StartupTaskXml.Build(Exe, User));
    static string Value(string name) => Doc().Descendants(Ns + name).Single().Value;

    [Fact]
    public void Declares_utf16_for_schtasks()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", StartupTaskXml.Build(Exe, User));
    }

    [Fact]
    public void Runs_elevated_at_logon_for_the_user()
    {
        Assert.Equal("HighestAvailable", Value("RunLevel"));
        Assert.Equal("InteractiveToken", Value("LogonType"));
        Assert.All(Doc().Descendants(Ns + "UserId"), e => Assert.Equal(User, e.Value));
        Assert.Single(Doc().Descendants(Ns + "LogonTrigger"));
    }

    [Fact]
    public void Has_no_time_limit_battery_stop_or_low_priority()
    {
        Assert.Equal("PT0S", Value("ExecutionTimeLimit"));
        Assert.Equal("false", Value("DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value("StopIfGoingOnBatteries"));
        Assert.Equal("false", Value("StopOnIdleEnd"));
        Assert.Equal("4", Value("Priority"));
    }

    [Fact]
    public void Command_round_trips_including_spaces_and_xml_characters()
    {
        Assert.Equal(Exe, StartupTaskXml.ReadCommand(StartupTaskXml.Build(Exe, User)));
        const string odd = @"C:\A & B <x>\ChildLock.exe";
        Assert.Equal(odd, StartupTaskXml.ReadCommand(StartupTaskXml.Build(odd, User)));
    }

    [Fact]
    public void ReadCommand_handles_garbage()
    {
        Assert.Null(StartupTaskXml.ReadCommand("not xml"));
    }
}

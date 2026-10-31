using System.Security.Cryptography;
using System.Text;
using ChildLock.Core;

namespace ChildLock;

/// <summary>Command-line options. Normal use needs none of them.</summary>
internal sealed record AppOptions
{
    public string SettingsPath { get; init; } = SettingsStore.DefaultPath;
    public bool CustomSettingsPath { get; init; }
    public string? DiagPath { get; init; }
    public string? ExportIconPath { get; init; }
    public SystemColorMode Theme { get; init; } = SystemColorMode.System;

    /// <summary>Don't ask for admin rights, even if the settings say to.</summary>
    public bool NoElevate { get; init; }

    /// <summary>Remove everything ChildLock added to Windows, then exit.</summary>
    public bool Uninstall { get; init; }

    /// <summary>Wait for this process to exit first (a restart with admin rights replacing the old copy).</summary>
    public int? ReplacePid { get; init; }

    /// <summary>The options worth passing on when ChildLock starts itself again.</summary>
    public IReadOnlyList<string> PassThrough { get; init; } = [];

    /// <summary>One instance per settings file, so a test copy can run beside the real one.</summary>
    public string MutexName => CustomSettingsPath
        ? @"Local\PureTech.ChildLock." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(SettingsPath).ToLowerInvariant())))[..16]
        : @"Local\PureTech.ChildLock";

    public static AppOptions Parse(string[] args)
    {
        var options = new AppOptions();
        var passThrough = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--settings" when Next() is { } path:
                    options = options with { SettingsPath = Path.GetFullPath(path), CustomSettingsPath = true };
                    passThrough.AddRange(["--settings", options.SettingsPath]);
                    break;
                case "--diag":
                    options = options with { DiagPath = "" };
                    passThrough.Add("--diag");
                    break;
                case "--theme" when Next() is { } theme:
                    options = options with
                    {
                        Theme = theme.ToLowerInvariant() switch
                        {
                            "dark" => SystemColorMode.Dark,
                            "light" => SystemColorMode.Classic,
                            _ => SystemColorMode.System,
                        },
                    };
                    passThrough.AddRange(["--theme", theme]);
                    break;
                case "--no-elevate":
                    options = options with { NoElevate = true };
                    break;
                case "--uninstall":
                    options = options with { Uninstall = true };
                    break;
                case "--replace" when Next() is { } pid && int.TryParse(pid, out int id):
                    options = options with { ReplacePid = id };
                    break;
                case "--export-icon" when Next() is { } path:
                    options = options with { ExportIconPath = path };
                    break;
            }
        }
        if (options.DiagPath == "")
            options = options with { DiagPath = Path.Combine(Path.GetDirectoryName(options.SettingsPath)!, "diag.log") };
        return options with { PassThrough = passThrough };
    }
}

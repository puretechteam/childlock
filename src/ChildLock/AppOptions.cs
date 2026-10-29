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

    /// <summary>One instance per settings file, so a test copy can run beside the real one.</summary>
    public string MutexName => CustomSettingsPath
        ? @"Local\PureTech.ChildLock." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(SettingsPath).ToLowerInvariant())))[..16]
        : @"Local\PureTech.ChildLock";

    public static AppOptions Parse(string[] args)
    {
        var options = new AppOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--settings" when Next() is { } path:
                    options = options with { SettingsPath = Path.GetFullPath(path), CustomSettingsPath = true };
                    break;
                case "--diag":
                    options = options with { DiagPath = "" };
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
                    break;
                case "--export-icon" when Next() is { } path:
                    options = options with { ExportIconPath = path };
                    break;
            }
        }
        if (options.DiagPath == "")
            options = options with { DiagPath = Path.Combine(Path.GetDirectoryName(options.SettingsPath)!, "diag.log") };
        return options;
    }
}

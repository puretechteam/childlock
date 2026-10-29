using System.Text.Encodings.Web;
using System.Text.Json;

namespace ChildLock.Core;

public static class SettingsStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        // Local file only, never embedded in HTML; keeps "Ctrl+Alt+L" readable instead of "Ctrl+Alt+L".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChildLock", "settings.json");

    /// <summary>Loads settings. A missing or unreadable file gives defaults; bad values are fixed.</summary>
    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options);
            return (settings ?? new Settings()).Normalize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    /// <summary>Writes to a temp file first, then swaps it in, so a crash never leaves half a file.</summary>
    public static void Save(string path, Settings settings)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalize(), Options));
        File.Move(temp, path, overwrite: true);
    }
}

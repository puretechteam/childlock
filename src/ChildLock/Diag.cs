namespace ChildLock;

/// <summary>Optional diagnostics log, turned on with --diag. Off by default.</summary>
internal static class Diag
{
    static string? _path;

    public static bool Enabled => _path is not null;

    public static void Enable(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _path = path;
        Log("diag started");
    }

    public static void Log(string message)
    {
        if (_path is null) return;
        try
        {
            File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never break the lock.
        }
    }
}

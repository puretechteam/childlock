using System.Text.Json.Serialization;

namespace ChildLock.Core;

/// <summary>User settings, stored as JSON.</summary>
public sealed record Settings
{
    public const int MaxCountdownSeconds = 10;
    public const int MaxIdleLockMinutes = 240;

    public string LockHotkey { get; init; } = Hotkey.Default.ToString();
    public string UnlockWord { get; init; } = Core.UnlockWord.Default;
    public int CountdownSeconds { get; init; } = 3;

    /// <summary>Lock after this many minutes without input. 0 turns it off.</summary>
    public int IdleLockMinutes { get; init; }

    public bool SoundEnabled { get; init; }
    public bool FirstRunDone { get; init; }

    [JsonIgnore]
    public Hotkey ParsedHotkey =>
        Hotkey.TryParse(LockHotkey, out var hotkey) ? hotkey.Value : Hotkey.Default;

    /// <summary>Replaces invalid values with defaults and clamps numbers into range.</summary>
    public Settings Normalize()
    {
        var defaults = new Settings();
        bool hotkeyOk = Hotkey.TryParse(LockHotkey, out var hotkey) && hotkey.Value.Validate() is null;
        bool wordOk = Core.UnlockWord.Validate(UnlockWord) is null;
        return this with
        {
            LockHotkey = hotkeyOk ? hotkey!.Value.ToString() : defaults.LockHotkey,
            UnlockWord = wordOk ? Core.UnlockWord.Normalize(UnlockWord) : defaults.UnlockWord,
            CountdownSeconds = Math.Clamp(CountdownSeconds, 0, MaxCountdownSeconds),
            IdleLockMinutes = Math.Clamp(IdleLockMinutes, 0, MaxIdleLockMinutes),
        };
    }
}

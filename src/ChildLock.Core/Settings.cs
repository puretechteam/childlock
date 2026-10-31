using System.Text.Json.Serialization;

namespace ChildLock.Core;

/// <summary>User settings, stored as JSON.</summary>
public sealed record Settings
{
    public const int MaxCountdownSeconds = 10;
    public const int MaxIdleLockMinutes = 240;
    public const int DefaultHoldSeconds = 3;
    public const int MinHoldSeconds = 2;
    public const int MaxHoldSeconds = 10;
    public const int MaxCleaningSeconds = 600;

    public string LockHotkey { get; init; } = Hotkey.Default.ToString();
    public string UnlockWord { get; init; } = Core.UnlockWord.Default;
    public int CountdownSeconds { get; init; } = 3;

    /// <summary>Lock after this many minutes without input. 0 turns it off.</summary>
    public int IdleLockMinutes { get; init; }

    /// <summary>What the hotkey, "Lock now" and auto-lock block.</summary>
    public LockMode DefaultLockMode { get; init; } = LockMode.Full;

    /// <summary>Hold the lock hotkey this many seconds to unlock. 0 turns it off.</summary>
    public int UnlockHoldSeconds { get; init; } = DefaultHoldSeconds;

    /// <summary>Show a dot on the badge for each correct character typed while locked.</summary>
    public bool ShowTypingProgress { get; init; }

    public bool ShowWordInTooltip { get; init; } = true;

    /// <summary>Show the "Locked" badge. The countdown badge always shows.</summary>
    public bool ShowBadge { get; init; } = true;

    /// <summary>Ask for the unlock word before opening Settings, pausing the hotkey or exiting.</summary>
    public bool RequireWordForSettings { get; init; }

    /// <summary>Ask Windows for admin rights at start. Turned off when you decline the prompt.</summary>
    public bool RunAsAdmin { get; init; } = true;

    /// <summary>Cleaning mode ends by itself after this many seconds. 0 means only when you unlock.</summary>
    public int CleaningAutoEndSeconds { get; init; } = 60;

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
            DefaultLockMode = Enum.IsDefined(DefaultLockMode) && DefaultLockMode.IsDefaultable() ? DefaultLockMode : LockMode.Full,
            UnlockHoldSeconds = UnlockHoldSeconds <= 0 ? 0 : Math.Clamp(UnlockHoldSeconds, MinHoldSeconds, MaxHoldSeconds),
            CleaningAutoEndSeconds = Math.Clamp(CleaningAutoEndSeconds, 0, MaxCleaningSeconds),
        };
    }
}

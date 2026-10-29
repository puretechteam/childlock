using System.Diagnostics.CodeAnalysis;

namespace ChildLock.Core;

/// <summary>Modifier flags. Values match the Win32 RegisterHotKey MOD_* constants.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A lock hotkey chord such as Ctrl+Alt+L.</summary>
public readonly record struct Hotkey(Modifiers Modifiers, int Key)
{
    public static readonly Hotkey Default = new(Modifiers.Ctrl | Modifiers.Alt, VirtualKeys.L);

    static readonly Dictionary<int, string> Names = BuildNames();
    static readonly Dictionary<string, int> KeysByName =
        Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>();
        for (int vk = VirtualKeys.A; vk <= VirtualKeys.Z; vk++) names[vk] = ((char)vk).ToString();
        for (int vk = VirtualKeys.D0; vk <= VirtualKeys.D9; vk++) names[vk] = ((char)vk).ToString();
        for (int vk = VirtualKeys.NumPad0; vk <= VirtualKeys.NumPad9; vk++) names[vk] = "Num" + (vk - VirtualKeys.NumPad0);
        for (int vk = VirtualKeys.F1; vk <= VirtualKeys.F24; vk++) names[vk] = "F" + (vk - VirtualKeys.F1 + 1);
        names[0x08] = "Backspace";
        names[0x0D] = "Enter";
        names[0x13] = "Pause";
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x2D] = "Insert";
        return names;
    }

    /// <summary>True if <paramref name="vk"/> can be the main key of a hotkey.</summary>
    public static bool IsSupportedKey(int vk) => Names.ContainsKey(vk);

    /// <summary>Returns null when valid, otherwise a user-facing reason.</summary>
    public string? Validate()
    {
        if (!IsSupportedKey(Key))
            return "Pick a letter, digit, F-key or navigation key.";
        if ((Modifiers & (Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win)) == 0)
            return "Use at least one of Ctrl, Alt or Win so normal typing can't trigger it.";
        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(Modifiers.Win)) parts.Add("Win");
        parts.Add(Names.TryGetValue(Key, out var name) ? name : $"0x{Key:X2}");
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        var mods = Modifiers.None;
        foreach (var token in tokens[..^1])
        {
            var mod = token.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Modifiers.Ctrl,
                "alt" => Modifiers.Alt,
                "shift" => Modifiers.Shift,
                "win" or "windows" => Modifiers.Win,
                _ => Modifiers.None,
            };
            if (mod == Modifiers.None || mods.HasFlag(mod)) return false;
            mods |= mod;
        }

        if (!KeysByName.TryGetValue(tokens[^1], out int key)) return false;
        hotkey = new Hotkey(mods, key);
        return true;
    }
}

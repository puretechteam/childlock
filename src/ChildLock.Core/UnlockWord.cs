namespace ChildLock.Core;

/// <summary>Rules for the secret unlock word or phrase.</summary>
public static class UnlockWord
{
    public const string Default = "unlock";
    public const int MaxLength = 64;

    // Common picks that someone (or a curious older kid) would try first. "unlock" is the published default.
    static readonly HashSet<string> Common =
    [
        "unlock", "password", "passw0rd", "letmein", "let me in", "open", "open sesame", "opensesame", "hello",
        "secret", "admin", "child", "childlock", "child lock", "kids", "baby", "mama", "papa", "mommy", "daddy",
        "iloveyou", "i love you", "welcome", "dragon", "monkey", "abc123", "test", "login", "please",
    ];

    const string KeyboardRows = "qwertyuiop asdfghjkl zxcvbnm 1234567890";

    /// <summary>Returns null when valid, otherwise a user-facing reason.</summary>
    public static string? Validate(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return "Type an unlock word or phrase.";
        string normalized = Normalize(word);
        if (normalized.Length > MaxLength)
            return $"The unlock word can be at most {MaxLength} characters long.";
        foreach (char c in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != ' ')
                return "The unlock word can only contain letters A-Z, digits 0-9 and spaces.";
        }
        return null;
    }

    /// <summary>Lower case, no leading or trailing spaces, single spaces between words.</summary>
    public static string Normalize(string word) =>
        string.Join(' ', word.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// A gentle warning for a valid but weak word, or null. Weak words are allowed; this only explains the risk.
    /// </summary>
    public static string? Weakness(string? word)
    {
        if (Validate(word) is not null) return null;
        string w = Normalize(word!);
        if (Common.Contains(w))
            return w == Default
                ? "This is the default word, which anyone can look up. Pick your own."
                : "This is a common choice that's easy to guess.";
        if (w.Length < 4)
            return "Short words can be hit by mashing keys. 6 or more characters is much safer.";
        if (w.Distinct().Count() <= 2)
            return "Using only one or two different keys makes it easy to hit by mashing.";
        if (IsSequence(w))
            return "Keyboard and number sequences are easy to hit by mashing and easy to guess.";
        return null;
    }

    static bool IsSequence(string w)
    {
        if (w.Contains(' ')) return false;
        if (KeyboardRows.Contains(w) || Reverse(KeyboardRows).Contains(w)) return true;
        // abcd, 4321, ...
        int step = w[1] - w[0];
        if (step is not (1 or -1)) return false;
        for (int i = 2; i < w.Length; i++)
        {
            if (w[i] - w[i - 1] != step) return false;
        }
        return true;
    }

    static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}

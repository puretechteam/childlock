namespace ChildLock.Core;

/// <summary>Rules for the secret unlock word.</summary>
public static class UnlockWord
{
    public const string Default = "unlock";
    public const int MinLength = 4;
    public const int MaxLength = 32;

    /// <summary>Returns null when valid, otherwise a user-facing reason.</summary>
    public static string? Validate(string? word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < MinLength || word.Length > MaxLength)
            return $"The unlock word must be {MinLength} to {MaxLength} characters long.";
        foreach (char c in word)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return "The unlock word can only contain letters A-Z and digits 0-9.";
        }
        return null;
    }

    public static string Normalize(string word) => word.ToLowerInvariant();
}

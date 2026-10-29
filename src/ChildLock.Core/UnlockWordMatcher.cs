namespace ChildLock.Core;

/// <summary>
/// Watches key presses and reports when the last keys typed spell the unlock word.
/// Letters, top-row digits and numpad digits count; every other key is ignored.
/// Matching is case-insensitive because only virtual-key codes are compared.
/// </summary>
public sealed class UnlockWordMatcher
{
    readonly char[] _word;
    readonly char[] _recent;
    int _count;

    public UnlockWordMatcher(string word)
    {
        if (UnlockWord.Validate(word) is { } error) throw new ArgumentException(error, nameof(word));
        _word = UnlockWord.Normalize(word).ToCharArray();
        _recent = new char[_word.Length];
    }

    /// <summary>Feeds one key-down. Returns true when this key completes the word.</summary>
    public bool Feed(int vk)
    {
        char c = ToChar(vk);
        if (c == '\0') return false;

        // Shift left by one; the word is at most 32 chars so this stays trivially cheap.
        if (_count == _recent.Length)
            Array.Copy(_recent, 1, _recent, 0, _recent.Length - 1);
        else
            _count++;
        _recent[_count - 1] = c;

        if (_count < _word.Length) return false;
        for (int i = 0; i < _word.Length; i++)
        {
            if (_recent[i] != _word[i]) return false;
        }
        Reset();
        return true;
    }

    public void Reset() => _count = 0;

    public static char ToChar(int vk) => vk switch
    {
        >= VirtualKeys.A and <= VirtualKeys.Z => (char)('a' + (vk - VirtualKeys.A)),
        >= VirtualKeys.D0 and <= VirtualKeys.D9 => (char)('0' + (vk - VirtualKeys.D0)),
        >= VirtualKeys.NumPad0 and <= VirtualKeys.NumPad9 => (char)('0' + (vk - VirtualKeys.NumPad0)),
        _ => '\0',
    };
}

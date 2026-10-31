namespace ChildLock.Tests;

public class UnlockWordMatcherTests
{
    static int Vk(char c) => c switch
    {
        >= 'a' and <= 'z' => VirtualKeys.A + (c - 'a'),
        >= 'A' and <= 'Z' => VirtualKeys.A + (c - 'A'),
        >= '0' and <= '9' => VirtualKeys.D0 + (c - '0'),
        ' ' => VirtualKeys.Space,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>Types the text and returns the indexes where a match fired.</summary>
    static List<int> Type(UnlockWordMatcher matcher, string text)
    {
        var hits = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            if (matcher.Feed(Vk(text[i]))) hits.Add(i);
        }
        return hits;
    }

    [Fact]
    public void Matches_exact_word_on_last_letter()
    {
        var m = new UnlockWordMatcher("unlock");
        Assert.Equal([5], Type(m, "unlock"));
    }

    [Theory]
    [InlineData("zzzzzzzunlock", 12)]
    [InlineData("ununlock", 7)]
    [InlineData("unlounlock", 9)]
    [InlineData("uunlock", 6)]
    public void Matches_after_noise_and_overlapping_prefixes(string typed, int expectedIndex)
    {
        Assert.Equal([expectedIndex], Type(new UnlockWordMatcher("unlock"), typed));
    }

    [Fact]
    public void Does_not_match_misspelling()
    {
        Assert.Empty(Type(new UnlockWordMatcher("unlock"), "unlcokunlokunloc"));
    }

    [Fact]
    public void Is_case_insensitive()
    {
        Assert.Equal([5], Type(new UnlockWordMatcher("UnLoCk"), "UNLOCK"));
        Assert.Equal([5], Type(new UnlockWordMatcher("unlock"), "UnLock"));
    }

    [Fact]
    public void Ignores_non_alphanumeric_keys_between_letters()
    {
        var m = new UnlockWordMatcher("unlock");
        int[] noise = [0x10, 0xA0, 0x0D, 0x5B, 0xAD, 0xB3]; // Space counts: phrases can contain spaces
        bool hit = false;
        foreach (char c in "unlock")
        {
            foreach (int n in noise) Assert.False(m.Feed(n));
            hit = m.Feed(Vk(c));
        }
        Assert.True(hit);
    }

    [Fact]
    public void Numpad_digits_count_as_digits()
    {
        var m = new UnlockWordMatcher("2468");
        Assert.False(m.Feed(VirtualKeys.NumPad0 + 2));
        Assert.False(m.Feed(VirtualKeys.D0 + 4));
        Assert.False(m.Feed(VirtualKeys.NumPad0 + 6));
        Assert.True(m.Feed(VirtualKeys.NumPad0 + 8));
    }

    [Fact]
    public void Reset_clears_partial_progress()
    {
        var m = new UnlockWordMatcher("unlock");
        Type(m, "unlo");
        m.Reset();
        Assert.Empty(Type(m, "ck"));
    }

    [Fact]
    public void Matches_again_after_a_match()
    {
        var m = new UnlockWordMatcher("abcd");
        Assert.Equal([3, 7], Type(m, "abcdabcd"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ünlock")]
    [InlineData("pass!")]
    public void Rejects_invalid_words(string word)
    {
        Assert.Throws<ArgumentException>(() => new UnlockWordMatcher(word));
    }

    [Fact]
    public void Phrases_with_spaces_match_including_the_space_bar()
    {
        var m = new UnlockWordMatcher("Open  Sesame");
        Assert.Equal([10], Type(m, "open sesame"));
        Assert.Empty(Type(m, "opensesame"));
    }

    [Fact]
    public void Single_character_words_match_on_that_key()
    {
        var m = new UnlockWordMatcher("x");
        Assert.Equal([2], Type(m, "abx"));
    }

    [Fact]
    public void Progress_counts_the_correct_characters_typed_so_far()
    {
        var m = new UnlockWordMatcher("unlock");
        List<int> progress = [];
        foreach (char c in "unlunlock")
        {
            m.Feed(Vk(c));
            progress.Add(m.Progress);
        }
        // u n l -> 1 2 3, then "u" breaks the run but starts a new one, ... and the final k matches and resets.
        Assert.Equal([1, 2, 3, 1, 2, 3, 4, 5, 0], progress);
    }

    [Fact]
    public void Progress_drops_to_zero_on_a_wrong_key()
    {
        var m = new UnlockWordMatcher("unlock");
        Type(m, "unl");
        Assert.Equal(3, m.Progress);
        m.Feed(Vk('z'));
        Assert.Equal(0, m.Progress);
    }

    [Fact]
    public void Progress_follows_overlapping_prefixes()
    {
        var m = new UnlockWordMatcher("abab");
        Type(m, "aba");
        Assert.Equal(3, m.Progress);
        m.Feed(Vk('a')); // "abaa": only the last "a" still fits
        Assert.Equal(1, m.Progress);
    }

    [Fact]
    public void Random_mashing_of_one_million_keys_never_unlocks()
    {
        // Simulates a kid mashing: uniform over every key on a full keyboard (~104 keys).
        int[] keyboard =
        [
            .. Enumerable.Range(VirtualKeys.A, 26),
            .. Enumerable.Range(VirtualKeys.D0, 10),
            .. Enumerable.Range(VirtualKeys.NumPad0, 10),
            .. Enumerable.Range(VirtualKeys.F1, 12),
            0x08, 0x09, 0x0D, 0x14, 0x1B, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
            0x2C, 0x2D, 0x2E, 0x5B, 0x5D, 0x6A, 0x6B, 0x6D, 0x6E, 0x6F, 0x90, 0x91,
            0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0,
            0xDB, 0xDC, 0xDD, 0xDE,
        ];
        var rng = new Random(20261029);
        var m = new UnlockWordMatcher(UnlockWord.Default);
        int unlocks = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            if (m.Feed(keyboard[rng.Next(keyboard.Length)])) unlocks++;
        }
        Assert.Equal(0, unlocks);
    }
}

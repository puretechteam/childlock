namespace ChildLock.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "childlock-tests-" + Guid.NewGuid().ToString("N"));
    string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = SettingsStore.Load(SettingsPath);
        Assert.Equal(new Settings(), s);
        Assert.Equal("Ctrl+Alt+L", s.LockHotkey);
        Assert.Equal("unlock", s.UnlockWord);
        Assert.Equal(3, s.CountdownSeconds);
        Assert.Equal(0, s.IdleLockMinutes);
        Assert.False(s.SoundEnabled);
        Assert.False(s.FirstRunDone);
    }

    [Fact]
    public void Round_trips()
    {
        var s = new Settings
        {
            LockHotkey = "Ctrl+Shift+F9",
            UnlockWord = "banana",
            CountdownSeconds = 7,
            IdleLockMinutes = 15,
            SoundEnabled = true,
            FirstRunDone = true,
        };
        SettingsStore.Save(SettingsPath, s);
        Assert.Equal(s, SettingsStore.Load(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Writes_readable_camel_case_json()
    {
        SettingsStore.Save(SettingsPath, new Settings());
        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"lockHotkey\": \"Ctrl+Alt+L\"", json);
        Assert.Contains("\"unlockWord\": \"unlock\"", json);
        Assert.DoesNotContain("parsedHotkey", json);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ not json");
        Assert.Equal(new Settings(), SettingsStore.Load(SettingsPath));
    }

    [Fact]
    public void Bad_values_are_fixed_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """
            {
              "lockHotkey": "Shift+L",
              "unlockWord": "no!",
              "countdownSeconds": 60,
              "idleLockMinutes": -3,
              "soundEnabled": true,
            }
            """);
        var s = SettingsStore.Load(SettingsPath);
        Assert.Equal("Ctrl+Alt+L", s.LockHotkey);
        Assert.Equal("unlock", s.UnlockWord);
        Assert.Equal(Settings.MaxCountdownSeconds, s.CountdownSeconds);
        Assert.Equal(0, s.IdleLockMinutes);
        Assert.True(s.SoundEnabled);
    }

    [Fact]
    public void Normalizes_word_case_and_hotkey_spelling()
    {
        var s = new Settings { UnlockWord = "BaNaNa", LockHotkey = "alt+ctrl+k" }.Normalize();
        Assert.Equal("banana", s.UnlockWord);
        Assert.Equal("Ctrl+Alt+K", s.LockHotkey);
    }

    [Theory]
    [InlineData("abcd", true)]
    [InlineData("abc", true)]
    [InlineData("a", true)]
    [InlineData("a1b2c3", true)]
    [InlineData("two words", true)]
    [InlineData("  open   sesame  ", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz01", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz012", false)]
    [InlineData("pass!", false)]
    [InlineData("ünlock", false)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    public void Unlock_word_rules(string word, bool valid)
    {
        Assert.Equal(valid, UnlockWord.Validate(word) is null);
    }

    [Fact]
    public void Phrases_are_trimmed_lowercased_and_single_spaced()
    {
        Assert.Equal("open sesame", UnlockWord.Normalize("  Open   SESAME "));
        Assert.Equal("open sesame", new Settings { UnlockWord = " Open  Sesame" }.Normalize().UnlockWord);
    }

    [Theory]
    [InlineData("unlock")]
    [InlineData("Password")]
    [InlineData("open sesame")]
    [InlineData("abc")]
    [InlineData("aaaaaa")]
    [InlineData("abababab")]
    [InlineData("1234")]
    [InlineData("987654")]
    [InlineData("abcdef")]
    [InlineData("qwerty")]
    [InlineData("asdf")]
    public void Weak_words_get_a_warning(string word)
    {
        Assert.NotNull(UnlockWord.Weakness(word));
    }

    [Theory]
    [InlineData("tiger7moon")]
    [InlineData("purple giraffe")]
    [InlineData("zebra")]
    [InlineData("pass!")] // invalid words get a validation error instead
    public void Reasonable_words_get_no_warning(string word)
    {
        Assert.Null(UnlockWord.Weakness(word));
    }

    [Fact]
    public void Loads_a_version_0_1_0_file_with_defaults_for_new_settings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """
            {
              "lockHotkey": "Ctrl+Alt+K",
              "unlockWord": "banana",
              "countdownSeconds": 5,
              "idleLockMinutes": 10,
              "soundEnabled": true,
              "firstRunDone": true
            }
            """);
        var s = SettingsStore.Load(SettingsPath);
        Assert.Equal("Ctrl+Alt+K", s.LockHotkey);
        Assert.Equal("banana", s.UnlockWord);
        Assert.Equal(LockMode.Full, s.DefaultLockMode);
        Assert.Equal(Settings.DefaultHoldSeconds, s.UnlockHoldSeconds);
        Assert.True(s.ShowBadge);
        Assert.True(s.ShowWordInTooltip);
        Assert.True(s.RunAsAdmin);
        Assert.False(s.ShowTypingProgress);
        Assert.False(s.RequireWordForSettings);
        Assert.Equal(60, s.CleaningAutoEndSeconds);
    }

    [Fact]
    public void Lock_mode_is_stored_as_readable_text()
    {
        SettingsStore.Save(SettingsPath, new Settings { DefaultLockMode = LockMode.KeyboardOnly });
        Assert.Contains("\"defaultLockMode\": \"keyboardOnly\"", File.ReadAllText(SettingsPath));
        Assert.Equal(LockMode.KeyboardOnly, SettingsStore.Load(SettingsPath).DefaultLockMode);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, Settings.MinHoldSeconds)]
    [InlineData(4, 4)]
    [InlineData(99, Settings.MaxHoldSeconds)]
    public void Hold_seconds_are_off_or_in_range(int value, int expected)
    {
        Assert.Equal(expected, new Settings { UnlockHoldSeconds = value }.Normalize().UnlockHoldSeconds);
    }

    [Fact]
    public void New_values_are_clamped()
    {
        var s = new Settings { CleaningAutoEndSeconds = 5000, DefaultLockMode = LockMode.Cleaning }.Normalize();
        Assert.Equal(Settings.MaxCleaningSeconds, s.CleaningAutoEndSeconds);
        Assert.Equal(LockMode.Full, s.DefaultLockMode);
        Assert.Equal(LockMode.Full, new Settings { DefaultLockMode = (LockMode)42 }.Normalize().DefaultLockMode);
        Assert.Equal(0, new Settings { CleaningAutoEndSeconds = -1 }.Normalize().CleaningAutoEndSeconds);
    }
}

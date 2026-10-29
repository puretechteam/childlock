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
    [InlineData("abc", false)]
    [InlineData("a1b2c3", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)]
    [InlineData("two words", false)]
    [InlineData("", false)]
    public void Unlock_word_rules(string word, bool valid)
    {
        Assert.Equal(valid, UnlockWord.Validate(word) is null);
    }
}

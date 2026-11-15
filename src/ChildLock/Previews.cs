using System.Drawing.Imaging;
using ChildLock.Core;

namespace ChildLock;

/// <summary>
/// Dev-time helpers behind <c>--export-shots</c> and <c>--preview</c>. The pictures in the README come
/// straight from the real drawing code, without locking anything or touching the running copy.
/// </summary>
internal static class Previews
{
    /// <summary>Writes the badge, cleaning screen and icon pictures as PNG files.</summary>
    public static void Export(string folder)
    {
        Directory.CreateDirectory(folder);

        (string Name, BadgeContent Content)[] badges =
        [
            ("badge-countdown", BadgeSet.Countdown(3)),
            ("badge-locked", BadgeSet.Locked(LockMode.Full)),
            ("badge-dots", BadgeSet.Locked(LockMode.Full) with { Dots = 3 }),
            ("badge-keyboard", BadgeSet.Locked(LockMode.KeyboardOnly)),
            ("badge-mouse", BadgeSet.Locked(LockMode.MouseOnly)),
            ("badge-holding", BadgeSet.Holding(0.6)),
        ];
        foreach (var (name, content) in badges)
        {
            using var badge = BadgeForm.Render(content, 2f);
            badge.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
        }

        using (var cleaning = new Bitmap(1280, 720, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(cleaning))
            {
                g.Clear(Color.Black);
                CleaningOverlay.PaintNote(g, cleaning.Size, 1.25f, "To finish, type your unlock word or hold Ctrl+Alt+L for 3 seconds.",
                    "Ends by itself in 0:42", note: 1, timeLeft: 0.7);
            }
            cleaning.Save(Path.Combine(folder, "cleaning.png"), ImageFormat.Png);
        }

        (string Name, bool Locked, Color? Color)[] icons =
        [
            ("ready", false, null), ("locked", true, null), ("paused", false, IconFactory.PausedColor), ("app", true, IconFactory.UnlockedColor),
        ];
        foreach (var (name, locked, color) in icons)
        {
            foreach (int size in new[] { 16, 24, 32, 64, 256 })
            {
                using var icon = IconFactory.Draw(size, locked, color);
                icon.Save(Path.Combine(folder, $"icon-{name}-{size}.png"), ImageFormat.Png);
            }
        }
    }

    /// <summary>One of the app's windows on its own, with default settings: "settings" or "prompt".</summary>
    public static Form? Window(string name) => name switch
    {
        "settings" => new SettingsForm(new Settings(), startAtLogin: false, _ => null),
        "prompt" => new WordPromptForm(UnlockWord.Default, "open Settings"),
        _ => null,
    };
}

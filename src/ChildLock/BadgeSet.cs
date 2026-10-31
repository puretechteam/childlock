using ChildLock.Core;

namespace ChildLock;

/// <summary>One badge per screen, all showing the same thing. Rebuilt when screens change.</summary>
internal sealed class BadgeSet : IDisposable
{
    readonly List<BadgeForm> _badges = [];
    BadgeContent? _base;     // countdown or "Locked"; null when no badge should show
    int _dots;
    double _hold;
    bool _holding;

    public void ShowCountdown(int seconds)
    {
        _base = new BadgeContent($"Locking in {seconds}...", "Press Esc to cancel", BadgeStyle.Countdown);
        _dots = 0;
        _holding = false;
        Render();
    }

    /// <param name="visible">False shows nothing until the hotkey is held or the word typed (if dots are on).</param>
    public void ShowLocked(LockMode mode, bool visible)
    {
        string title = mode switch
        {
            LockMode.KeyboardOnly => "Keyboard locked",
            LockMode.MouseOnly => "Mouse locked",
            _ => "Locked",
        };
        _base = visible ? new BadgeContent(title, "", BadgeStyle.Locked) : null;
        _dots = 0;
        _holding = false;
        Render();
    }

    /// <summary>Characters of the unlock word typed so far; 0 hides the dots.</summary>
    public void SetDots(int dots)
    {
        if (dots == _dots) return;
        _dots = dots;
        Render();
    }

    /// <summary>Progress (0 to 1) of holding the hotkey to unlock, or not holding.</summary>
    public void SetHold(bool holding, double progress)
    {
        if (holding == _holding && Math.Abs(progress - _hold) < 0.01) return;
        _holding = holding;
        _hold = progress;
        Render();
    }

    public void Hide()
    {
        _base = null;
        _dots = 0;
        _holding = false;
        foreach (var badge in _badges) badge.Hide();
    }

    public void EnsureTopmost()
    {
        foreach (var badge in _badges) badge.EnsureTopmost();
    }

    /// <summary>Screens were added, removed or rearranged.</summary>
    public void Rebuild()
    {
        DisposeBadges();
        Render();
    }

    void Render()
    {
        BadgeContent? content = _holding
            ? new BadgeContent("Keep holding to unlock...", "", BadgeStyle.Holding, Progress: Math.Max(0.02, _hold))
            : _dots > 0
                ? (_base ?? new BadgeContent("Locked", "", BadgeStyle.Locked)) with { Dots = _dots }
                : _base;

        if (content is null)
        {
            foreach (var badge in _badges) badge.Hide();
            return;
        }
        if (_badges.Count == 0)
        {
            foreach (var screen in Screen.AllScreens) _badges.Add(new BadgeForm(screen));
        }
        foreach (var badge in _badges) badge.ShowContent(content);
    }

    void DisposeBadges()
    {
        foreach (var badge in _badges) badge.Dispose();
        _badges.Clear();
    }

    public void Dispose() => DisposeBadges();
}

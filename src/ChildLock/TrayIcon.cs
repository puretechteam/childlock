using System.Collections.Concurrent;
using PureTech.Tray;
using Menu = PureTech.Tray.Menu;

namespace ChildLock;

/// <summary>
/// The notification-area icon, its menu and notifications, through PureTech.Tray on its own thread.
/// Tray calls can wait on Explorer (or on a busy tray thread for up to 10 s), and ChildLock's UI thread
/// also runs the input hooks, so every call is handed to one background worker and the UI thread never
/// waits. Icon and tooltip changes that pile up are merged into one update.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const string AppId = "PureTech.ChildLock";
    const int MaxTooltip = 127;

    readonly Tray _tray;
    readonly BlockingCollection<Action> _work = [];
    readonly Thread _worker;
    readonly Lock _gate = new();
    Rgba? _icon;
    string? _tooltip;
    bool _flushQueued;

    static TrayIcon() => Tray.Log += (message, e) => Diag.Log("tray: " + message + (e is null ? "" : " " + e.Message));

    public TrayIcon(Rgba icon, string tooltip, Menu menu, MenuTheme theme)
    {
        _tray = new Tray(icon, Clip(tooltip), menu)
        {
            AppId = AppId,
            AppName = "ChildLock",
            AppIcon = IconFactory.AppIconPath(),
            Theme = theme,
            OnClick = _ => { }, // a left click does nothing; the menu is on the right button
        };
        _tray.Start();
        _worker = new Thread(Work) { IsBackground = true, Name = "childlock-tray" };
        _worker.Start();
    }

    public void Update(Rgba? icon = null, string? tooltip = null)
    {
        lock (_gate)
        {
            if (icon is not null) _icon = icon;
            if (tooltip is not null) _tooltip = Clip(tooltip);
            if (_flushQueued) return;
            _flushQueued = true;
        }
        Enqueue(() =>
        {
            Rgba? nextIcon;
            string? nextTooltip;
            lock (_gate)
            {
                (nextIcon, nextTooltip) = (_icon, _tooltip);
                (_icon, _tooltip, _flushQueued) = (null, null, false);
            }
            _tray.Update(nextIcon, nextTooltip);
            _tray.RefreshMenu();
        });
    }

    /// <summary>A toast with optional buttons, or a balloon if toasts are off. Actions run on the tray thread.</summary>
    public void Notify(string title, string text, IReadOnlyList<(string Label, Action Run)>? actions = null, Action? onClick = null) =>
        Enqueue(() => _tray.Notify(title, text, actions, onClick));

    void Enqueue(Action call)
    {
        if (!_work.IsAddingCompleted) _work.TryAdd(call);
    }

    void Work()
    {
        foreach (var call in _work.GetConsumingEnumerable())
        {
            try
            {
                call();
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                Diag.Log("tray call failed: " + e.Message);
            }
        }
    }

    static string Clip(string text) => text.Length <= MaxTooltip ? text : text[..(MaxTooltip - 3)] + "...";

    public void Dispose()
    {
        _work.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _tray.Stop();
        _work.Dispose();
    }
}

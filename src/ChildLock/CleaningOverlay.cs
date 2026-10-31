using System.Drawing.Text;
using static ChildLock.Native.NativeMethods;

namespace ChildLock;

/// <summary>
/// Cleaning mode's black screens, one per monitor, so smudges and dust show up. A short note on the
/// main screen fades to a faint grey after a few seconds. With an auto-end time, the screens fade back
/// in over the last few seconds and then <see cref="TimeUp"/> fires.
/// </summary>
internal sealed class CleaningOverlay : IDisposable
{
    const int NoteBrightMs = 3000;
    const int NoteFadeMs = 1000;
    const double NoteDim = 0.14;
    const int FadeBackMs = 3000;

    readonly List<CleaningForm> _forms = [];
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    long _startedAt;
    int _autoEndMs;
    string _hint = "";

    public event Action? TimeUp;

    public CleaningOverlay() => _timer.Tick += (_, _) => Tick();

    public bool Visible => _forms.Count > 0;

    /// <param name="autoEndSeconds">0 to stay until unlocked.</param>
    /// <param name="hint">How to finish, e.g. "Type your unlock word, or hold Ctrl+Alt+L for 3 seconds".</param>
    public void Show(int autoEndSeconds, string hint)
    {
        Hide();
        _autoEndMs = autoEndSeconds * 1000;
        _hint = hint;
        _startedAt = Environment.TickCount64;
        CreateForms();
        Tick();
        _timer.Start();
    }

    /// <summary>Screens changed: cover the new set, keeping the time already spent.</summary>
    public void Rebuild()
    {
        if (!Visible) return;
        foreach (var form in _forms) form.Dispose();
        _forms.Clear();
        CreateForms();
        Tick();
    }

    void CreateForms()
    {
        foreach (var screen in Screen.AllScreens)
        {
            var form = new CleaningForm(screen, showNote: screen.Primary);
            _forms.Add(form);
            form.Show();
        }
    }

    public void Hide()
    {
        _timer.Stop();
        foreach (var form in _forms) form.Dispose();
        _forms.Clear();
    }

    public void EnsureTopmost()
    {
        foreach (var form in _forms) form.EnsureTopmost();
    }

    void Tick()
    {
        long elapsed = Environment.TickCount64 - _startedAt;
        double note = elapsed < NoteBrightMs ? 1 : Math.Max(NoteDim, 1 - (elapsed - NoteBrightMs) / (double)NoteFadeMs);
        double opacity = 1;
        string remaining = "";
        if (_autoEndMs > 0)
        {
            long left = _autoEndMs - elapsed;
            if (left <= 0)
            {
                _timer.Stop();
                TimeUp?.Invoke();
                return;
            }
            long seconds = (left + 999) / 1000;
            remaining = $"Ends by itself in {seconds / 60}:{seconds % 60:00}";
            if (left < FadeBackMs) opacity = left / (double)FadeBackMs;
        }
        foreach (var form in _forms) form.ShowState(_hint, remaining, note, opacity);
    }

    public void Dispose()
    {
        Hide();
        _timer.Dispose();
    }

    sealed class CleaningForm : Form
    {
        readonly Screen _screen;
        readonly bool _showNote;
        string _hint = "";
        string _remaining = "";
        double _note = 1;

        public CleaningForm(Screen screen, bool showNote)
        {
            _screen = screen;
            _showNote = showNote;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            AllowTransparency = true; // layered from the start, so fading back in never re-creates the window
            Text = "ChildLock cleaning mode";
            Bounds = screen.Bounds;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ShowState(string hint, string remaining, double note, double opacity)
        {
            if (Math.Abs(Opacity - opacity) > 0.005) Opacity = opacity;
            if (!_showNote || (hint == _hint && remaining == _remaining && Math.Abs(note - _note) < 0.005)) return;
            _hint = hint;
            _remaining = remaining;
            _note = note;
            Invalidate();
        }

        public void EnsureTopmost()
        {
            if (IsHandleCreated && Visible)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Bounds = _screen.Bounds; // always the whole screen, whatever size Windows suggests
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_showNote) return;
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int bright = (int)(230 * _note);
            int dim = (int)(160 * _note);
            using var titleFont = new Font("Segoe UI Semibold", 20f, FontStyle.Regular, GraphicsUnit.Point);
            using var lineFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            using var titleBrush = new SolidBrush(Color.FromArgb(bright, bright, bright));
            using var lineBrush = new SolidBrush(Color.FromArgb(dim, dim, dim));
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

            string[] lines = _remaining.Length > 0 ? [_hint, _remaining] : [_hint];
            var titleSize = g.MeasureString("Cleaning mode", titleFont);
            float lineHeight = lineFont.GetHeight(g) * 1.3f;
            float total = titleSize.Height + lineHeight * lines.Length;
            float y = (ClientSize.Height - total) / 2;
            var column = new RectangleF(0, y, ClientSize.Width, titleSize.Height);
            g.DrawString("Cleaning mode", titleFont, titleBrush, column, center);
            y += titleSize.Height;
            foreach (string line in lines)
            {
                g.DrawString(line, lineFont, lineBrush, new RectangleF(0, y, ClientSize.Width, lineHeight), center);
                y += lineHeight;
            }
        }
    }
}

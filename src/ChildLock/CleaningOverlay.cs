using System.Drawing.Drawing2D;
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
        double timeLeft = 0;
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
            timeLeft = left / (double)_autoEndMs;
            if (left < FadeBackMs) opacity = left / (double)FadeBackMs;
        }
        foreach (var form in _forms) form.ShowState(_hint, remaining, note, timeLeft, opacity);
    }

    /// <summary>The note in the middle of the main screen: a padlock in a ring that empties as time runs out, and how to finish.</summary>
    /// <param name="s">Screen scale: 1 at 100%.</param>
    /// <param name="note">Brightness of the note, 1 down to a faint grey.</param>
    /// <param name="timeLeft">Share of the auto-end time still left, 0 when there is none.</param>
    public static void PaintNote(Graphics g, Size screen, float s, string hint, string remaining, double note, double timeLeft)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var bright = Grey(232 * note);
        var dim = Grey(160 * note);
        using var titleFont = new Font("Segoe UI Semibold", 27f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var lineFont = new Font("Segoe UI", 15f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(bright);
        using var lineBrush = new SolidBrush(dim);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

        string[] lines = remaining.Length > 0 ? [hint, remaining] : [hint];
        float ring = 96 * s;
        float gap = 26 * s;
        var titleSize = g.MeasureString("Cleaning mode", titleFont);
        float lineHeight = lineFont.GetHeight(g) * 1.35f;
        float y = (screen.Height - (ring + gap + titleSize.Height + lineHeight * lines.Length)) / 2;

        var circle = new RectangleF((screen.Width - ring) / 2, y, ring, ring);
        using (var track = new Pen(Grey(52 * note), 3 * s))
            g.DrawEllipse(track, circle);
        if (timeLeft > 0)
        {
            using var arc = new Pen(bright, 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, circle, -90, (float)(360 * Math.Min(1, timeLeft)));
        }
        IconFactory.DrawPadlock(g, RectangleF.Inflate(circle, -20 * s, -20 * s), bright, open: false, bold: false, keyhole: Color.Black);
        y += ring + gap;

        g.DrawString("Cleaning mode", titleFont, titleBrush, new RectangleF(0, y, screen.Width, titleSize.Height), center);
        y += titleSize.Height;
        foreach (string line in lines)
        {
            g.DrawString(line, lineFont, lineBrush, new RectangleF(0, y, screen.Width, lineHeight), center);
            y += lineHeight;
        }

        static Color Grey(double level) => Color.FromArgb((int)level, (int)level, (int)level);
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
        double _timeLeft;

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

        public void ShowState(string hint, string remaining, double note, double timeLeft, double opacity)
        {
            if (Math.Abs(Opacity - opacity) > 0.005) Opacity = opacity;
            // The ring only needs a repaint when it has moved by a visible amount (a few times a second).
            if (!_showNote || (hint == _hint && remaining == _remaining && Math.Abs(note - _note) < 0.005 && Math.Abs(timeLeft - _timeLeft) < 0.004)) return;
            _hint = hint;
            _remaining = remaining;
            _note = note;
            _timeLeft = timeLeft;
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
            if (_showNote) PaintNote(e.Graphics, ClientSize, DeviceDpi / 96f, _hint, _remaining, _note, _timeLeft);
        }
    }
}

namespace PixelTunes.Companion;

/// <summary>
/// "Now Playing" popup. It is a separate always-on-top window that never takes focus and
/// ignores the mouse, so it can float over WoW (in Windowed/borderless mode) without
/// interrupting the game.
/// </summary>
internal sealed class ToastForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private Image? _art;
    private string _title = "", _artist = "", _state = "";
    private double _userScale = 1;
    private double? _bar;
    private DateTime _hideAt;
    private const double MaxOpacity = 0.92;

    public ToastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = ToastRenderer.Background;
        Opacity = 0;
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Animate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                          Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)); // Windows 11 only; harmless elsewhere
    }

    public void ShowNowPlaying(NowPlaying np, bool trackChanged, Rectangle area, AppSettings settings)
    {
        _art?.Dispose();
        _art = np.Art;
        _title = np.Title;
        _artist = np.Artist;
        _state = np.IsPlaying ? (trackChanged ? "Now playing" : "Playing") : "Paused";
        _bar = null;
        Present(area, settings, settings.ToastSeconds);
    }

    /// <summary>Shows the volume level (or why it couldn't be changed).</summary>
    public void ShowVolume(VolumeResult result, Rectangle area, AppSettings settings)
    {
        _art?.Dispose();
        _art = null;
        string app = string.IsNullOrEmpty(result.AppName) ? "" : $" \u00B7 {result.AppName}";
        _state = "Volume" + app;
        if (result.Ok)
        {
            _title = $"{result.Percent}%";
            _artist = "";
            _bar = result.Percent / 100.0;
        }
        else
        {
            _title = "Couldn't change volume";
            _artist = result.Message;
            _bar = null;
        }
        // Volume changes come in quick bursts, so this one goes away sooner (errors stay longer to be readable).
        Present(area, settings, result.Ok ? Math.Min(1.5, settings.ToastSeconds) : settings.ToastSeconds);
    }

    private void Present(Rectangle area, AppSettings settings, double seconds)
    {
        _userScale = settings.ToastScale;

        var size = ToastRenderer.SizeFor(DeviceDpi, _userScale);
        var pos = ToastRenderer.Locate(area, size, settings, DeviceDpi);

        _hideAt = DateTime.UtcNow.AddSeconds(seconds);
        if (!Visible)
        {
            Opacity = 0;
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
        }
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, pos.X, pos.Y, size.Width, size.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Invalidate();
        _timer.Start();
    }

    private void Animate()
    {
        bool fadingOut = DateTime.UtcNow >= _hideAt;
        if (!fadingOut && Opacity < MaxOpacity) Opacity = Math.Min(MaxOpacity, Opacity + 0.12);
        else if (fadingOut)
        {
            Opacity = Math.Max(0, Opacity - 0.06);
            if (Opacity <= 0)
            {
                _timer.Stop();
                Hide();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) =>
        ToastRenderer.Paint(e.Graphics, ClientRectangle, DeviceDpi, _userScale, _state, _title, _artist, _art, _bar);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _art?.Dispose();
        }
        base.Dispose(disposing);
    }
}

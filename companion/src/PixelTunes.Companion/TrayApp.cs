using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using PixelTunes.Core;

namespace PixelTunes.Companion;

/// <summary>
/// The companion's main loop: about 20 times a second it captures the top-left corner of the
/// WoW window, decodes the addon's pixel strip, and turns new button presses into Windows
/// media commands. It never sends input to WoW and never reads WoW's memory or files.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "PixelTunes";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly WowWindowFinder _finder = new();
    private readonly ScreenSampler _sampler = new();
    private readonly CommandTracker _tracker = new(requiredStableReads: 2);
    private readonly MediaController _media;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Control _invoker = new();
    private readonly ToastForm _toast = new();
    private DiagnosticsForm? _diagnostics;
    private PopupPlacementForm? _placement;
    private string _lastTitle = "Song title", _lastArtist = "Artist";

    // State shown in diagnostics.
    private WowWindow? _wow;
    private Rectangle? _wowRect;
    private string _wowStatus = "searching...";
    private DecodeResult _lastResult = DecodeResult.NotFound("not started");
    private int _blockSizeHint;
    private bool _stripWasFound;
    private string _lastCommand = "none yet";
    private string _nowPlaying = "unknown";
    private string _mixer = "not checked yet (shown while Diagnostics is open)";
    private DateTime _nextMixerCheck = DateTime.MinValue;
    private bool _mixerCheckRunning;
    private double _avgTickMs;
    private readonly Stopwatch _rateWatch = Stopwatch.StartNew();
    private int _ticksThisSecond;
    private double _ticksPerSecond;

    public TrayApp(bool openDiagnostics = false, bool openPlacement = false)
    {
        _ = _invoker.Handle; // create the handle so background events can be marshalled to the UI thread
        _media = new MediaController(_settings);
        _media.NowPlayingChanged += (np, changed) => _invoker.BeginInvoke(() => OnNowPlaying(np, changed));

        _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        var toastItem = new ToolStripMenuItem("Show \"Now Playing\" popups") { Checked = _settings.ShowNowPlayingToast, CheckOnClick = true };
        toastItem.CheckedChanged += (_, _) => { _settings.ShowNowPlayingToast = toastItem.Checked; _settings.Save(); };
        // Which player the buttons control. Shown as two radio-style choices.
        var spotifyOnlyItem = new ToolStripMenuItem("Spotify only") { Checked = !_settings.ControlAnyMediaPlayer };
        var anyPlayerItem = new ToolStripMenuItem("Any media player") { Checked = _settings.ControlAnyMediaPlayer };
        anyPlayerItem.ToolTipText = "Uses Spotify when it's open, otherwise whatever is playing (YouTube in a browser, Apple Music, ...)";
        void SetAnyPlayer(bool any)
        {
            if (_settings.ControlAnyMediaPlayer == any) return;
            _settings.ControlAnyMediaPlayer = any;
            _settings.Save();
            spotifyOnlyItem.Checked = !any;
            anyPlayerItem.Checked = any;
            _media.Refresh();
            Log.Info($"Control mode: {(any ? "any media player" : "Spotify only")}");
            _ = RefreshNowPlayingTextAsync();
        }
        spotifyOnlyItem.Click += (_, _) => SetAnyPlayer(false);
        anyPlayerItem.Click += (_, _) => SetAnyPlayer(true);
        var controlItem = new ToolStripMenuItem("Control");
        controlItem.DropDownItems.Add(spotifyOnlyItem);
        controlItem.DropDownItems.Add(anyPlayerItem);
        controlItem.DropDownOpening += (_, _) =>
        {
            spotifyOnlyItem.Checked = !_settings.ControlAnyMediaPlayer;
            anyPlayerItem.Checked = _settings.ControlAnyMediaPlayer;
        };

        var startupItem = new ToolStripMenuItem("Start with Windows") { Checked = IsStartWithWindows(), CheckOnClick = true };
        startupItem.CheckedChanged += (_, _) => SetStartWithWindows(startupItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"PixelTunes {Program.Version}") { Enabled = false });
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Diagnostics...", null, (_, _) => ShowDiagnostics());
        menu.Items.Add(controlItem);
        menu.Items.Add(toastItem);
        menu.Items.Add("Move popup...", null, (_, _) => ShowPlacement());
        menu.Items.Add(startupItem);
        menu.Items.Add("Open settings file", null, (_, _) => OpenInExplorer(AppSettings.FilePath, select: true));
        menu.Items.Add("Open log folder", null, (_, _) => OpenInExplorer(Log.Folder, select: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => _statusItem.Text = ShortStatus();

        _tray = new NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = "PixelTunes",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowDiagnostics();

        _timer = new System.Windows.Forms.Timer { Interval = _settings.PollIntervalMs };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _ = InitializeMediaAsync();
        if (openDiagnostics) ShowDiagnostics();
        if (openPlacement) ShowPlacement();
    }

    private async Task InitializeMediaAsync()
    {
        await _media.InitializeAsync();
        await RefreshNowPlayingTextAsync();
    }

    // ---------------------------------------------------------------- main loop

    private void Tick()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            Feed(Sample());
        }
        catch (Exception ex)
        {
            Log.Error("Tick failed", ex);
        }
        _avgTickMs = _avgTickMs * 0.95 + sw.Elapsed.TotalMilliseconds * 0.05;

        _ticksThisSecond++;
        if (_rateWatch.ElapsedMilliseconds >= 1000)
        {
            _ticksPerSecond = _ticksThisSecond * 1000.0 / _rateWatch.ElapsedMilliseconds;
            _ticksThisSecond = 0;
            _rateWatch.Restart();
        }
    }

    private DecodeResult Sample()
    {
        _wow = _finder.Get();
        if (_wow == null)
        {
            _wowStatus = "not found - is WoW running?";
            _wowRect = null;
            return DecodeResult.NotFound("WoW not running");
        }
        if (Native.IsIconic(_wow.Handle))
        {
            _wowStatus = "minimized";
            return DecodeResult.NotFound("WoW minimized");
        }

        _wowRect = Native.GetClientScreenRect(_wow.Handle);
        if (_wowRect is not { Width: > 0, Height: > 0 } rect)
        {
            _wowStatus = "window has no client area";
            return DecodeResult.NotFound("no client area");
        }
        _wowStatus = "found";

        var img = _sampler.Capture(rect);
        if (img == null) return DecodeResult.NotFound("screen capture unavailable (locked screen / UAC prompt?)");

        var result = PixelDecoder.Decode(img.Value, _blockSizeHint);
        if (result.Found) _blockSizeHint = result.BlockSize;
        return result;
    }

    private void Feed(DecodeResult result)
    {
        _lastResult = result;
        if (result.Found != _stripWasFound)
        {
            _stripWasFound = result.Found;
            Log.Info(result.Found
                ? $"Pixel strip found (block size {result.BlockSize}px, seq {result.Sequence})"
                : $"Pixel strip lost: {result.Detail}");
        }

        var command = _tracker.Process(result);
        if (command != null) _ = ExecuteAsync(command.Value, "WoW");
    }

    private async Task ExecuteAsync(MediaCommand command, string source)
    {
        var sw = Stopwatch.StartNew();
        if (command is MediaCommand.VolumeUp or MediaCommand.VolumeDown)
        {
            var result = await _media.AdjustVolumeAsync(up: command == MediaCommand.VolumeUp);
            string outcome = result.Ok ? $"{result.AppName} volume {result.Percent}%" : result.Message;
            _lastCommand = $"{DateTime.Now:HH:mm:ss} {command} (from {source}) -> {outcome} in {sw.ElapsedMilliseconds} ms";
            Log.Info($"Command {command} from {source}: {outcome}");
            ShowVolumeToast(result, fromWow: source == "WoW");
            return;
        }

        string mediaOutcome = await _media.ExecuteAsync(command);
        _lastCommand = $"{DateTime.Now:HH:mm:ss} {command} (from {source}) -> {mediaOutcome} in {sw.ElapsedMilliseconds} ms";
        Log.Info($"Command {command} from {source}: {mediaOutcome}");
        await RefreshNowPlayingTextAsync(delayMs: 600);
    }

    private void ShowVolumeToast(VolumeResult result, bool fromWow)
    {
        if (!_settings.ShowNowPlayingToast) return;
        bool wowFocused = _wow != null && Native.GetForegroundWindow() == _wow.Handle;
        // Buttons in the diagnostics window always show it, so you can see the result there too.
        if (fromWow && _settings.ToastOnlyWhenWowFocused && !wowFocused) return;
        var area = _wowRect ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        _toast.ShowVolume(result, area, _settings);
    }

    // ---------------------------------------------------------------- now playing

    private async Task RefreshNowPlayingTextAsync(int delayMs = 0)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        var np = await _media.GetNowPlayingAsync();
        _nowPlaying = np == null ? "nothing / unknown" : $"{np.Title} - {np.Artist} ({(np.IsPlaying ? "playing" : "paused")})";
        if (np != null) { _lastTitle = np.Title; _lastArtist = np.Artist; }
        np?.Art?.Dispose();
    }

    private void OnNowPlaying(NowPlaying np, bool trackChanged)
    {
        _nowPlaying = $"{np.Title} - {np.Artist} ({(np.IsPlaying ? "playing" : "paused")})";
        _lastTitle = np.Title;
        _lastArtist = np.Artist;

        bool wowFocused = _wow != null && Native.GetForegroundWindow() == _wow.Handle;
        if (!_settings.ShowNowPlayingToast || (_settings.ToastOnlyWhenWowFocused && !wowFocused))
        {
            np.Art?.Dispose();
            return;
        }

        var area = _wowRect ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        _toast.ShowNowPlaying(np, trackChanged, area, _settings); // the toast takes ownership of the art
    }

    // ---------------------------------------------------------------- diagnostics & menu

    private string ShortStatus()
    {
        if (_wow == null) return "Waiting for WoW...";
        if (!_lastResult.Found) return "WoW found - addon strip not visible";
        return "Connected to WoW addon";
    }

    private string FullStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PixelTunes companion {Program.Version}");
        sb.AppendLine();
        sb.AppendLine($"WoW window    : {_wowStatus}");
        if (_wow != null)
        {
            sb.AppendLine($"                \"{_wow.Title}\"  process={_wow.ProcessName}  class={_wow.ClassName}");
            if (_wowRect is { } r)
                sb.AppendLine($"                client area {r.Width}x{r.Height} at ({r.X},{r.Y}), focused={(Native.GetForegroundWindow() == _wow.Handle ? "yes" : "no")}");
        }
        sb.AppendLine();
        if (_lastResult.Found)
        {
            sb.AppendLine($"Pixel strip   : FOUND  block size {_lastResult.BlockSize}px, sequence {_lastResult.Sequence}, last frame command={_lastResult.Command}");
        }
        else
        {
            sb.AppendLine($"Pixel strip   : not found - {_lastResult.Detail}");
            if (_lastResult.BlockSize > 0) sb.AppendLine($"                (best guess block size {_lastResult.BlockSize}px)");
        }
        if (_lastResult.RawBlocks.Length > 0)
            sb.AppendLine($"                blocks read: {string.Join(" ", _lastResult.RawBlocks)}   (expected 5 3 x x x x 6)");
        sb.AppendLine($"Tracker       : baseline sequence {(_tracker.BaselineSequence?.ToString() ?? "none (waiting for a stable frame)")}");
        sb.AppendLine();
        sb.AppendLine($"Media         : {_media.Status}");
        sb.AppendLine($"Now playing   : {_nowPlaying}");
        sb.AppendLine($"Last command  : {_lastCommand}");
        sb.AppendLine($"Volume mixer  : {_mixer}");
        RefreshMixerInBackground();
        sb.AppendLine();
        sb.AppendLine($"Sampling      : {_ticksPerSecond:0.0}/s, {_avgTickMs:0.00} ms per sample");
        sb.AppendLine($"Settings      : {AppSettings.FilePath}");
        sb.AppendLine($"Log           : {Log.FilePath}");
        return sb.ToString();
    }

    /// <summary>Lists the volume-mixer entries every few seconds while Diagnostics is open.</summary>
    private async void RefreshMixerInBackground()
    {
        if (_mixerCheckRunning || DateTime.UtcNow < _nextMixerCheck) return;
        _mixerCheckRunning = true;
        try
        {
            var entries = await AppVolumeController.ListAsync();
            var target = _media.TargetAppId;
            var matcher = target == null ? null : AudioAppMatcher.ForMediaApp(target);
            _mixer = entries.Count == 0
                ? "no apps found"
                : string.Join("\n                ", entries.Select(e =>
                    $"{e.ProcessName} {(e.Volume < 0 ? "?" : VolumeMath.ToPercent(e.Volume) + "%")}" +
                    (matcher != null && matcher.Matches(e.ProcessName, "") ? " (controlled)" : "") +
                    $"  [{e.Access}]"));
        }
        catch (Exception ex)
        {
            _mixer = $"error: {ex.Message}";
        }
        finally
        {
            _nextMixerCheck = DateTime.UtcNow.AddSeconds(3);
            _mixerCheckRunning = false;
        }
    }

    private void ShowDiagnostics()
    {
        if (_diagnostics is { IsDisposed: false })
        {
            _diagnostics.Activate();
            return;
        }
        _diagnostics = new DiagnosticsForm(
            FullStatus,
            () => _sampler.LastCapture,
            async cmd => await ExecuteAsync(cmd, "diagnostics button"),
            () =>
            {
                var path = _sampler.SaveSnapshot();
                Log.Info($"Snapshot saved: {path}\n{FullStatus()}");
                return path;
            });
        _diagnostics.Show();
    }

    private void ShowPlacement()
    {
        if (_placement is { IsDisposed: false })
        {
            _placement.Activate();
            return;
        }
        // Place relative to the WoW window when it's open, otherwise the main screen.
        var wow = _finder.Get();
        Rectangle? wowRect = wow != null && !Native.IsIconic(wow.Handle) ? Native.GetClientScreenRect(wow.Handle) : null;
        var area = wowRect is { Width: > 0, Height: > 0 } r ? r : Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        if (wowRect == null)
            _tray.ShowBalloonTip(5000, "PixelTunes", "WoW isn't open, so the popup position will be relative to your main screen. For the best result, open WoW first.", ToolTipIcon.Info);

        _placement = new PopupPlacementForm(area, _settings, _lastTitle, _lastArtist);
        _placement.Saved += () => _tray.ShowBalloonTip(3000, "PixelTunes", "Popup position saved.", ToolTipIcon.Info);
        _placement.Show();
    }

    private static void OpenInExplorer(string path, bool select)
    {
        try
        {
            Process.Start("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"");
        }
        catch (Exception ex)
        {
            Log.Error("Could not open Explorer", ex);
        }
    }

    private static bool IsStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private static void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
            Log.Info($"Start with Windows: {enable}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not change startup setting", ex);
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _diagnostics?.Close();
        _placement?.Close();
        _toast.Dispose();
        _media.Dispose();
        _sampler.Dispose();
        _invoker.Dispose();
        base.ExitThreadCore();
    }
}

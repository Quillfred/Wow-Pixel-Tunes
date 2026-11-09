using PixelTunes.Core;
using Windows.Media.Control;

namespace PixelTunes.Companion;

internal sealed record NowPlaying(string Title, string Artist, bool IsPlaying, Image? Art, string SourceApp);

/// <summary>
/// Controls Spotify through the Windows media controls (the same system your keyboard's
/// media keys and the volume flyout use). No Spotify login or Premium needed.
/// </summary>
internal sealed class MediaController : IDisposable
{
    private readonly AppSettings _settings;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _watched;
    private string _lastTrackKey = "";
    private bool? _lastPlaying;

    /// <summary>Raised (on a background thread) when the track or play/pause state changes.</summary>
    public event Action<NowPlaying, bool /*trackChanged*/>? NowPlayingChanged;

    public MediaController(AppSettings settings) => _settings = settings;

    public string Status { get; private set; } = "starting...";

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => WatchTargetSession();
            _manager.CurrentSessionChanged += (_, _) => WatchTargetSession();
            WatchTargetSession();
            Log.Info("Windows media controls ready");
        }
        catch (Exception ex)
        {
            Status = "Windows media controls unavailable (needs Windows 10 1809 or newer)";
            Log.Error("Could not open Windows media controls", ex);
        }
    }

    private GlobalSystemMediaTransportControlsSession? FindTarget()
    {
        if (_manager == null) return null;
        try
        {
            var spotify = _manager.GetSessions()
                .FirstOrDefault(s => s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase));
            if (spotify != null) return spotify;
            return _settings.ControlAnyMediaPlayer ? _manager.GetCurrentSession() : null;
        }
        catch (Exception ex)
        {
            Log.Error("Listing media sessions failed", ex);
            return null;
        }
    }

    /// <summary>Re-picks the session to control, e.g. after the "Control" setting changes.</summary>
    public void Refresh() => WatchTargetSession();

    private void WatchTargetSession()
    {
        if (_manager == null) return; // keep the "unavailable" status from InitializeAsync
        var target = FindTarget();
        if (target == null)
        {
            Status = _settings.ControlAnyMediaPlayer
                ? "no media app found (is Spotify open?)"
                : "Spotify not found (is it open? see README troubleshooting)";
        }
        else
        {
            Status = $"connected to {target.SourceAppUserModelId}";
        }

        if (ReferenceEquals(target, _watched)) return;
        if (_watched != null)
        {
            _watched.MediaPropertiesChanged -= OnMediaChanged;
            _watched.PlaybackInfoChanged -= OnPlaybackChanged;
        }
        _watched = target;
        if (_watched != null)
        {
            Log.Info($"Watching media session: {_watched.SourceAppUserModelId}");
            _watched.MediaPropertiesChanged += OnMediaChanged;
            _watched.PlaybackInfoChanged += OnPlaybackChanged;
            _ = PublishNowPlayingAsync(initial: true);
        }
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = PublishNowPlayingAsync();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = PublishNowPlayingAsync();

    public async Task<string> ExecuteAsync(MediaCommand command)
    {
        var session = FindTarget();
        if (session == null)
        {
            WatchTargetSession();
            return Status;
        }

        try
        {
            bool ok = command switch
            {
                MediaCommand.PlayPause => await session.TryTogglePlayPauseAsync(),
                MediaCommand.Next => await session.TrySkipNextAsync(),
                MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                _ => false,
            };
            return ok ? "ok" : $"{session.SourceAppUserModelId} refused the command";
        }
        catch (Exception ex)
        {
            Log.Error($"{command} failed", ex);
            return $"failed: {ex.Message}";
        }
    }

    /// <summary>Turns the controlled player's own volume-mixer slider up or down one step.</summary>
    public async Task<VolumeResult> AdjustVolumeAsync(bool up)
    {
        var session = FindTarget();
        if (session == null)
        {
            WatchTargetSession();
            return new VolumeResult(false, -1, "", Status);
        }
        return await AppVolumeController.AdjustAsync(session.SourceAppUserModelId, up, _settings.VolumeStepPercent);
    }

    /// <summary>The app id of the player being controlled, or null.</summary>
    public string? TargetAppId => FindTarget()?.SourceAppUserModelId;

    public async Task<NowPlaying?> GetNowPlayingAsync()
    {
        var session = _watched ?? FindTarget();
        if (session == null) return null;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrEmpty(props.Title)) return null;
            bool playing = session.GetPlaybackInfo()?.PlaybackStatus ==
                           GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Image? art = null;
            if (props.Thumbnail != null)
            {
                try
                {
                    using var ras = await props.Thumbnail.OpenReadAsync();
                    using var stream = ras.AsStreamForRead();
                    using var copy = new MemoryStream();
                    await stream.CopyToAsync(copy);
                    copy.Position = 0;
                    using var img = Image.FromStream(copy);
                    art = new Bitmap(img);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Album art unavailable: {ex.Message}");
                }
            }
            return new NowPlaying(props.Title, props.Artist ?? "", playing, art, session.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            Log.Warn($"Reading now playing failed: {ex.Message}");
            return null;
        }
    }

    private async Task PublishNowPlayingAsync(bool initial = false)
    {
        // Spotify updates title and art in a couple of steps; wait for them to settle.
        await Task.Delay(250);
        var np = await GetNowPlayingAsync();
        if (np == null) return;

        string key = np.Title + "\u0001" + np.Artist;
        bool trackChanged = key != _lastTrackKey;
        bool playChanged = np.IsPlaying != _lastPlaying;
        _lastTrackKey = key;
        _lastPlaying = np.IsPlaying;

        if (initial || (!trackChanged && !playChanged))
        {
            np.Art?.Dispose();
            return;
        }
        NowPlayingChanged?.Invoke(np, trackChanged);
    }

    public void Dispose()
    {
        if (_watched != null)
        {
            _watched.MediaPropertiesChanged -= OnMediaChanged;
            _watched.PlaybackInfoChanged -= OnPlaybackChanged;
        }
    }
}

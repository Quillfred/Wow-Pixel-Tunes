using System.Text.Json;

namespace PixelTunes.Companion;

/// <summary>User settings stored in %APPDATA%\PixelTunes\settings.json (edit while the app is closed).</summary>
internal sealed class AppSettings
{
    /// <summary>Show a small "Now Playing" popup over WoW when the track or play state changes.</summary>
    public bool ShowNowPlayingToast { get; set; } = true;

    /// <summary>Only show the popup while WoW is the active window.</summary>
    public bool ToastOnlyWhenWowFocused { get; set; } = true;

    /// <summary>TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter or BottomRight (relative to the WoW window).</summary>
    public string ToastPosition { get; set; } = "TopCenter";

    /// <summary>Distance in pixels from the top/bottom edge of the WoW window.</summary>
    public int ToastOffsetY { get; set; } = 80;

    public double ToastSeconds { get; set; } = 4;

    /// <summary>Set by "Move popup...": use ToastCenterX/Y instead of ToastPosition/ToastOffsetY.</summary>
    public bool ToastCustomPosition { get; set; }

    /// <summary>Centre of the popup as a fraction of the WoW window (0 = left/top, 1 = right/bottom).</summary>
    public double ToastCenterX { get; set; } = 0.5;
    public double ToastCenterY { get; set; } = 0.1;

    /// <summary>Popup size multiplier (1 = normal).</summary>
    public double ToastScale { get; set; } = 1;

    /// <summary>How much each volume button press changes the player's volume-mixer slider, in percent.</summary>
    public int VolumeStepPercent { get; set; } = 5;

    /// <summary>If Spotify isn't running, control whatever media app Windows considers current.</summary>
    public bool ControlAnyMediaPlayer { get; set; }

    /// <summary>How often to look at the WoW window, in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelTunes");

    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                {
                    s.PollIntervalMs = Math.Clamp(s.PollIntervalMs, 20, 500);
                    s.ToastSeconds = Math.Clamp(s.ToastSeconds, 1, 30);
                    s.VolumeStepPercent = Math.Clamp(s.VolumeStepPercent, 1, 25);
                    s.ToastCenterX = Math.Clamp(s.ToastCenterX, 0, 1);
                    s.ToastCenterY = Math.Clamp(s.ToastCenterY, 0, 1);
                    s.ToastScale = Math.Clamp(s.ToastScale, PopupPlacementForm.MinScale, PopupPlacementForm.MaxScale);
                    s.Save(); // writes any new settings into older files
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        var defaults = new AppSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}

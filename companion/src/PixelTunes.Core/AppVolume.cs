namespace PixelTunes.Core;

/// <summary>Volume step arithmetic for the per-app volume buttons.</summary>
public static class VolumeMath
{
    /// <summary>
    /// Moves <paramref name="current"/> (0..1) one step up or down, snapping to the step grid so
    /// repeated presses land on round numbers (e.g. 63% -> 65% -> 70%).
    /// </summary>
    public static float Step(float current, bool up, int stepPercent)
    {
        stepPercent = Math.Clamp(stepPercent, 1, 50);
        double pct = Math.Clamp(current, 0f, 1f) * 100.0;
        // Treat values within 0.5% of a grid line as being on it (float rounding).
        double gridIndex = pct / stepPercent;
        double snapped = up ? Math.Floor(gridIndex + 0.005) + 1 : Math.Ceiling(gridIndex - 0.005) - 1;
        return (float)Math.Clamp(snapped * stepPercent / 100.0, 0.0, 1.0);
    }

    public static int ToPercent(float level) => (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100);
}

/// <summary>
/// Works out which entries in the Windows volume mixer belong to the media app being
/// controlled. Media apps are identified by an "app user model ID" (e.g. "Spotify.exe",
/// "Chrome", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") while audio sessions are
/// identified by process name and a session identifier string, so this maps between them.
/// </summary>
public sealed class AudioAppMatcher
{
    // Processes whose volume must never be changed by PixelTunes (the game itself).
    private static readonly string[] NeverTouch = ["wow", "wowclassic", "wowt", "wowb", "wowclassict", "wowclassicb"];

    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "chrome",
        ["msedge"] = "msedge",
        ["brave"] = "brave",
        ["opera"] = "opera",
        ["vivaldi"] = "vivaldi",
        ["308046B0AF4A39CB"] = "firefox", // Firefox's ID for a default install
    };

    public IReadOnlyList<string> ProcessNames { get; }
    public IReadOnlyList<string> IdentifierFragments { get; }
    public string DisplayName { get; }

    private AudioAppMatcher(List<string> processNames, List<string> fragments, string displayName)
    {
        ProcessNames = processNames;
        IdentifierFragments = fragments;
        DisplayName = displayName;
    }

    public static AudioAppMatcher ForMediaApp(string appUserModelId)
    {
        var id = (appUserModelId ?? "").Trim();
        var names = new List<string>();
        var fragments = new List<string>();
        void AddName(string n)
        {
            n = n.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            if (n.Length > 0 && !names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
        }

        string display = id;
        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            AddName("Spotify"); // desktop and Microsoft Store versions both run Spotify.exe
            display = "Spotify";
        }
        else if (KnownApps.TryGetValue(id, out var known))
        {
            AddName(known);
            display = char.ToUpperInvariant(known[0]) + known[1..];
        }
        else if (id.Contains('!'))
        {
            // Packaged (Store) app: "Publisher.AppName_hash!EntryPoint". The audio session
            // identifier contains the package's full name, which starts with "Publisher.AppName_".
            var family = id[..id.IndexOf('!')];
            int underscore = family.LastIndexOf('_');
            var package = underscore > 0 ? family[..underscore] : family;
            fragments.Add(package);
            var lastDot = package.LastIndexOf('.');
            display = lastDot >= 0 ? package[(lastDot + 1)..] : package;
        }
        else
        {
            // Plain desktop app, usually "Something.exe" or a full path.
            var file = Path.GetFileName(id.Replace('/', '\\').Split('\\').Last());
            AddName(file);
            display = names.FirstOrDefault() ?? id;
        }

        return new AudioAppMatcher(names, fragments, display);
    }

    /// <summary>Whether an audio session (by its process name and instance identifier) belongs to this app.</summary>
    public bool Matches(string processName, string sessionInstanceIdentifier)
    {
        if (NeverTouch.Contains(processName, StringComparer.OrdinalIgnoreCase)) return false;
        if (ProcessNames.Any(n => string.Equals(n, processName, StringComparison.OrdinalIgnoreCase))) return true;
        return IdentifierFragments.Any(f =>
            (sessionInstanceIdentifier ?? "").Contains(f, StringComparison.OrdinalIgnoreCase));
    }
}

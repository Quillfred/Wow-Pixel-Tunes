using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PixelTunes.Companion;

internal sealed record WowWindow(IntPtr Handle, string ProcessName, string ClassName, string Title);

/// <summary>
/// Locates the WoW game window (Retail, Classic or other flavors) without touching the game
/// process: it only looks at top-level window class names and process names.
/// </summary>
internal sealed partial class WowWindowFinder
{
    // WoW's game window class: "waApplication Window" in current clients (seen on Retail 12.x),
    // "GxWindowClass" / "GxWindowClassD3d" in older ones.
    private static readonly string[] WowWindowClassPrefixes = ["waApplication Window", "GxWindowClass"];

    // Wow.exe, WowT.exe (PTR), WowB.exe (beta), WowClassic.exe, WowClassicT.exe, ...
    [GeneratedRegex(@"^wow(classic)?(t|b|beta)?(-64)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WowProcessName();

    private WowWindow? _current;
    private DateTime _nextSearch = DateTime.MinValue;
    private readonly Dictionary<uint, string> _processNames = new();

    /// <summary>Returns the WoW window, re-scanning at most every 2 seconds while none is found.</summary>
    public WowWindow? Get()
    {
        if (_current != null && Native.IsWindow(_current.Handle))
        {
            // If another WoW window is now in front (two clients running), follow it.
            var fg = Native.GetForegroundWindow();
            if (fg != _current.Handle && fg != IntPtr.Zero && Describe(fg) is { } other && IsWow(other))
            {
                _current = other;
                Log.Info($"Switched to WoW window: {other}");
            }
            return _current;
        }

        if (_current != null)
        {
            Log.Info("WoW window closed");
            _current = null;
        }

        if (DateTime.UtcNow < _nextSearch) return null;
        _nextSearch = DateTime.UtcNow.AddSeconds(2);

        var found = new List<WowWindow>();
        Native.EnumWindows((hWnd, _) =>
        {
            if (Native.IsWindowVisible(hWnd) && Describe(hWnd) is { } w && IsWow(w)) found.Add(w);
            return true;
        }, IntPtr.Zero);

        if (found.Count == 0) return null;
        var foreground = Native.GetForegroundWindow();
        // Prefer the window in front, then ones whose process is named like WoW.
        _current = found
            .OrderByDescending(w => w.Handle == foreground)
            .ThenByDescending(w => WowProcessName().IsMatch(w.ProcessName))
            .First();
        Log.Info($"Found WoW window: {_current}" + (found.Count > 1 ? $" ({found.Count} candidates)" : ""));
        return _current;
    }

    private WowWindow? Describe(IntPtr hWnd)
    {
        try
        {
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            if (!_processNames.TryGetValue(pid, out var name))
            {
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch { name = ""; }
                _processNames[pid] = name;
            }
            return new WowWindow(hWnd, name, Native.GetClassName(hWnd), Native.GetWindowText(hWnd));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWow(WowWindow w) =>
        WowWindowClassPrefixes.Any(p => w.ClassName.StartsWith(p, StringComparison.Ordinal)) ||
        WowProcessName().IsMatch(w.ProcessName);
}

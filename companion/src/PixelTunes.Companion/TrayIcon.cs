using System.Reflection;

namespace PixelTunes.Companion;

internal static class TrayIcon
{
    /// <summary>Loads the embedded app icon (a fresh copy each call, so callers can dispose it).</summary>
    public static Icon Create()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("PixelTunes.ico", StringComparison.OrdinalIgnoreCase));
        if (name != null)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            return new Icon(stream, SystemInformation.SmallIconSize);
        }
        return (Icon)SystemIcons.Application.Clone();
    }
}

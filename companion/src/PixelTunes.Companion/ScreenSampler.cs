using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PixelTunes.Core;

namespace PixelTunes.Companion;

/// <summary>
/// Copies the small top-left corner of the WoW window from the screen (read-only; the same
/// thing a screenshot tool does). Only the region that can contain the strip is captured.
/// </summary>
internal sealed class ScreenSampler : IDisposable
{
    public const int CaptureWidth = Protocol.BlockCount * Protocol.MaxBlockSize + 8;
    public const int CaptureHeight = Protocol.MaxBlockSize + 4;

    private readonly Bitmap _bitmap = new(CaptureWidth, CaptureHeight, PixelFormat.Format32bppArgb);
    private readonly Graphics _graphics;
    private byte[] _buffer = [];

    public ScreenSampler()
    {
        _graphics = Graphics.FromImage(_bitmap);
    }

    /// <summary>Last captured region, for the diagnostics window and snapshots.</summary>
    public Bitmap LastCapture => _bitmap;

    public BgraImage? Capture(Rectangle clientScreenRect)
    {
        int w = Math.Min(CaptureWidth, clientScreenRect.Width);
        int h = Math.Min(CaptureHeight, clientScreenRect.Height);
        if (w <= 0 || h <= 0) return null;

        try
        {
            _graphics.Clear(Color.Black);
            _graphics.CopyFromScreen(clientScreenRect.X, clientScreenRect.Y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        catch (Exception)
        {
            // Happens briefly on the secure desktop (Ctrl+Alt+Del, UAC prompt), while locked, etc.
            return null;
        }

        var data = _bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int size = data.Stride * h;
            if (_buffer.Length != size) _buffer = new byte[size];
            Marshal.Copy(data.Scan0, _buffer, 0, size);
            return new BgraImage(_buffer, w, h, data.Stride);
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }
    }

    /// <summary>Saves the last capture (original and 8x enlarged) for troubleshooting.</summary>
    public string SaveSnapshot()
    {
        var folder = Path.Combine(Log.Folder, "snapshots");
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var raw = Path.Combine(folder, $"capture-{stamp}.png");
        _bitmap.Save(raw, ImageFormat.Png);

        using var zoom = new Bitmap(CaptureWidth * 8, CaptureHeight * 8);
        using (var g = Graphics.FromImage(zoom))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_bitmap, new Rectangle(0, 0, zoom.Width, zoom.Height));
        }
        var zoomed = Path.Combine(folder, $"capture-{stamp}-zoom8x.png");
        zoom.Save(zoomed, ImageFormat.Png);
        return zoomed;
    }

    public void Dispose()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
    }
}

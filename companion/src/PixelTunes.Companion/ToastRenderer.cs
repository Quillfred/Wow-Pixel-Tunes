namespace PixelTunes.Companion;

/// <summary>Drawing, sizing and placement of the "Now Playing" popup, shared by the popup and the move/resize preview.</summary>
internal static class ToastRenderer
{
    public static readonly Color Background = Color.FromArgb(24, 24, 24);
    public static readonly Color Accent = Color.FromArgb(30, 215, 96);
    private const int BaseWidth = 380, BaseHeight = 76;

    public static Size SizeFor(int deviceDpi, double userScale)
    {
        double s = deviceDpi / 96.0 * userScale;
        return new Size((int)Math.Round(BaseWidth * s), (int)Math.Round(BaseHeight * s));
    }

    /// <summary>Where the popup goes inside <paramref name="area"/> (the WoW window, or the screen).</summary>
    public static Point Locate(Rectangle area, Size size, AppSettings settings, int deviceDpi)
    {
        int x, y;
        if (settings.ToastCustomPosition)
        {
            x = area.Left + (int)Math.Round(settings.ToastCenterX * area.Width) - size.Width / 2;
            y = area.Top + (int)Math.Round(settings.ToastCenterY * area.Height) - size.Height / 2;
        }
        else
        {
            int margin = (int)(24 * deviceDpi / 96.0);
            x = settings.ToastPosition switch
            {
                var p when p.EndsWith("Left", StringComparison.OrdinalIgnoreCase) => area.Left + margin,
                var p when p.EndsWith("Right", StringComparison.OrdinalIgnoreCase) => area.Right - size.Width - margin,
                _ => area.Left + (area.Width - size.Width) / 2,
            };
            y = settings.ToastPosition.StartsWith("Bottom", StringComparison.OrdinalIgnoreCase)
                ? area.Bottom - size.Height - settings.ToastOffsetY
                : area.Top + settings.ToastOffsetY;
        }
        // Keep it fully inside the area.
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Point(x, y);
    }

    public static void Paint(Graphics g, Rectangle bounds, int deviceDpi, double userScale,
        string state, string title, string artist, Image? art, double? bar = null)
    {
        float s = (float)(deviceDpi / 96.0 * userScale);
        float f = (float)userScale; // font sizes are in points, which already follow the screen DPI
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(Background)) g.FillRectangle(bg, bounds);
        using (var accent = new SolidBrush(Accent)) g.FillRectangle(accent, bounds.Left, bounds.Top, (int)(4 * s), bounds.Height);

        int pad = (int)(10 * s), artSize = bounds.Height - 2 * pad;
        int textLeft = bounds.Left + pad + (int)(4 * s);
        if (art != null)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(art, new Rectangle(textLeft, bounds.Top + pad, artSize, artSize));
            textLeft += artSize + pad;
        }

        int textWidth = bounds.Right - textLeft - pad;
        int top = bounds.Top;
        using var stateFont = new Font("Segoe UI", 8f * f, FontStyle.Bold);
        using var titleFont = new Font("Segoe UI Semibold", 11f * f);
        using var artistFont = new Font("Segoe UI", 9f * f);
        const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, state.ToUpperInvariant(), stateFont,
            new Rectangle(textLeft, top + pad - (int)(2 * s), textWidth, (int)(16 * s)), Accent, flags);
        TextRenderer.DrawText(g, title, titleFont,
            new Rectangle(textLeft, top + pad + (int)(13 * s), textWidth, (int)(24 * s)), Color.White, flags);
        if (bar is { } level)
        {
            // Volume bar in place of the artist line.
            int barTop = top + pad + (int)(40 * s), barHeight = Math.Max(3, (int)(6 * s));
            using var trackBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
            using var fillBrush = new SolidBrush(Accent);
            g.FillRectangle(trackBrush, textLeft, barTop, textWidth, barHeight);
            g.FillRectangle(fillBrush, textLeft, barTop, (int)(textWidth * Math.Clamp(level, 0, 1)), barHeight);
        }
        else
        {
            TextRenderer.DrawText(g, artist, artistFont,
                new Rectangle(textLeft, top + pad + (int)(35 * s), textWidth, (int)(20 * s)), Color.FromArgb(179, 179, 179), flags);
        }
    }
}

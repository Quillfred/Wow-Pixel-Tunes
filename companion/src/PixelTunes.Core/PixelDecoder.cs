namespace PixelTunes.Core;

/// <summary>A captured image region in 32-bit BGRA layout (what GDI / LockBits produce on Windows).</summary>
public readonly struct BgraImage
{
    public readonly byte[] Data;
    public readonly int Width;
    public readonly int Height;
    public readonly int Stride;

    public BgraImage(byte[] data, int width, int height, int stride)
    {
        Data = data; Width = width; Height = height; Stride = stride;
    }

    public static BgraImage Create(int width, int height) => new(new byte[width * height * 4], width, height, width * 4);

    public (byte R, byte G, byte B) GetPixel(int x, int y)
    {
        int i = y * Stride + x * 4;
        return (Data[i + 2], Data[i + 1], Data[i]);
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        int i = y * Stride + x * 4;
        Data[i] = b; Data[i + 1] = g; Data[i + 2] = r; Data[i + 3] = 255;
    }

    public int Quantized(int x, int y)
    {
        var (r, g, b) = GetPixel(x, y);
        return Protocol.Quantize(r, g, b);
    }
}

public sealed record DecodeResult(
    bool Found,
    int BlockSize,
    MediaCommand Command,
    int Sequence,
    int[] RawBlocks,
    string Detail)
{
    public static DecodeResult NotFound(string detail, int[]? raw = null, int blockSize = 0) =>
        new(false, blockSize, MediaCommand.None, -1, raw ?? [], detail);
}

/// <summary>
/// Finds and decodes the addon's pixel strip in a captured image whose (0,0) is the
/// top-left corner of the WoW window's client area.
/// </summary>
public static class PixelDecoder
{
    /// <summary>
    /// Decodes the strip. <paramref name="hintBlockSize"/> (e.g. the last size that worked)
    /// is tried first so the common case costs only a handful of pixel reads.
    /// </summary>
    public static DecodeResult Decode(BgraImage img, int hintBlockSize = 0)
    {
        if (img.Width < Protocol.BlockCount || img.Height < 1)
            return DecodeResult.NotFound("capture too small");

        // Fast exit: the strip always starts with a magenta block at the very corner. If
        // there is no magenta in the first few rows, the addon is not showing (WoW is
        // loading, alt-tabbed, covered by another window, or the addon is off).
        var runs = new List<int>();
        for (int row = 0; row < Math.Min(4, img.Height); row++) runs.Add(MagentaRun(img, row));
        if (runs.All(r => r == 0))
            return DecodeResult.NotFound("no start marker in the top-left corner");

        // Candidate block sizes: the hint first, then the measured magenta run lengths
        // (+/- 1 for edge blending), then every other size as a last resort.
        var candidates = new List<int>();
        void Add(int b)
        {
            if (b >= 1 && b <= Protocol.MaxBlockSize && !candidates.Contains(b)) candidates.Add(b);
        }
        Add(hintBlockSize);
        foreach (int run in runs.Where(r => r > 0)) { Add(run); Add(run - 1); Add(run + 1); }
        int measuredCount = candidates.Count;
        for (int b = 1; b <= Protocol.MaxBlockSize; b++) Add(b);

        DecodeResult? firstFailure = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var r = TryDecode(img, candidates[i]);
            if (r.Found) return r;
            // Report the failure for the most likely size, which is the useful one in diagnostics.
            if (i < measuredCount) firstFailure ??= r;
        }
        return firstFailure ?? DecodeResult.NotFound("strip not found");
    }

    private static int MagentaRun(BgraImage img, int row)
    {
        int x = 0;
        while (x < img.Width && img.Quantized(x, row) == Protocol.StartMarkerA) x++;
        return x;
    }

    /// <summary>Tries to decode assuming each block is <paramref name="b"/> pixels square.</summary>
    public static DecodeResult TryDecode(BgraImage img, int b)
    {
        if (b * Protocol.BlockCount > img.Width || b > img.Height)
            return DecodeResult.NotFound($"block size {b} does not fit in capture", blockSize: b);

        var raw = new int[Protocol.BlockCount];
        for (int i = 0; i < Protocol.BlockCount; i++)
        {
            int x0 = i * b, cy = b / 2;
            int center = img.Quantized(x0 + b / 2, cy);
            // For blocks of 3+ pixels, require the block to be uniform away from its
            // edges. This rejects a wrong block size that happens to line up by chance.
            if (b >= 3)
            {
                int inset = b >= 6 ? 2 : 1;
                int yTop = Math.Min(inset, b - 1), yBot = Math.Max(b - 1 - inset, 0);
                int[] samples =
                [
                    img.Quantized(x0 + inset, yTop),
                    img.Quantized(x0 + b - 1 - inset, yTop),
                    img.Quantized(x0 + inset, yBot),
                    img.Quantized(x0 + b - 1 - inset, yBot),
                ];
                if (samples.Any(s => s != center))
                {
                    raw[i] = center;
                    return DecodeResult.NotFound($"block {i} is not uniform at size {b}", raw[..(i + 1)], b);
                }
            }
            raw[i] = center;
        }

        if (raw[0] != Protocol.StartMarkerA || raw[1] != Protocol.StartMarkerB)
            return DecodeResult.NotFound("start markers do not match", raw, b);
        if (raw[6] != Protocol.EndMarker)
            return DecodeResult.NotFound("end marker does not match", raw, b);
        if (raw[5] != Protocol.Checksum(raw[2], raw[3], raw[4]))
            return DecodeResult.NotFound("checksum mismatch", raw, b);
        if (raw[2] > Protocol.MaxCommand)
            return DecodeResult.NotFound($"unknown command {raw[2]}", raw, b);

        return new DecodeResult(true, b, (MediaCommand)raw[2], raw[3] * 8 + raw[4], raw, "ok");
    }
}

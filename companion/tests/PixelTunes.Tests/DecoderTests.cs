using PixelTunes.Core;
using Xunit;

namespace PixelTunes.Tests;

public class DecoderTests
{
    /// <summary>Renders block values the way the addon draws them, on top of a busy "game" background.</summary>
    internal static BgraImage Render(int[] values, int blockSize, int width = 160, int height = 32, int seed = 1,
        int offColor = 0, int onColor = 255)
    {
        var img = BgraImage.Create(width, height);
        var rng = new Random(seed);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img.SetPixel(x, y, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));

        for (int i = 0; i < values.Length; i++)
        {
            var (r, g, b) = Protocol.ColorOf(values[i]);
            byte Map(byte c) => (byte)(c == 255 ? onColor : offColor);
            for (int y = 0; y < blockSize; y++)
                for (int x = 0; x < blockSize; x++)
                    img.SetPixel(i * blockSize + x, y, Map(r), Map(g), Map(b));
        }
        return img;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(16)]
    public void DecodesEveryCommandAndSequence_AtBlockSize(int blockSize)
    {
        for (int cmd = 0; cmd <= Protocol.MaxCommand; cmd++)
            for (int seq = 0; seq < Protocol.SeqModulo; seq++)
            {
                var img = Render(Protocol.Encode(cmd, seq), blockSize, seed: cmd * 100 + seq);
                var r = PixelDecoder.Decode(img);
                Assert.True(r.Found, $"cmd {cmd} seq {seq} size {blockSize}: {r.Detail}");
                Assert.Equal((MediaCommand)cmd, r.Command);
                Assert.Equal(seq, r.Sequence);
                Assert.Equal(blockSize, r.BlockSize);
            }
    }

    [Fact]
    public void ToleratesColorShift_FromGammaNightLightOrHdr()
    {
        var img = Render(Protocol.Encode(2, 37), 4, offColor: 40, onColor: 200);
        var r = PixelDecoder.Decode(img);
        Assert.True(r.Found, r.Detail);
        Assert.Equal(MediaCommand.Next, r.Command);
        Assert.Equal(37, r.Sequence);
    }

    [Fact]
    public void ToleratesBlurredEdges_WhenStripIsNotPixelAligned()
    {
        var img = Render(Protocol.Encode(1, 9), 6);
        // Smear the boundary between every pair of blocks.
        for (int i = 1; i < Protocol.BlockCount; i++)
            for (int y = 0; y < 6; y++)
                img.SetPixel(i * 6, y, 128, 128, 128);
        var r = PixelDecoder.Decode(img);
        Assert.True(r.Found, r.Detail);
        Assert.Equal(MediaCommand.PlayPause, r.Command);
        Assert.Equal(9, r.Sequence);
    }

    [Fact]
    public void ReportsNotFound_WhenNoStrip()
    {
        var img = BgraImage.Create(160, 32); // all black: e.g. loading screen
        Assert.False(PixelDecoder.Decode(img).Found);

        var noisy = Render([], 4, seed: 7);
        for (int s = 0; s < 200; s++)
        {
            var bg = Render([], 4, seed: s);
            Assert.False(PixelDecoder.Decode(bg).Found, $"false positive on random background {s}");
        }
    }

    [Fact]
    public void RejectsCorruptedFrame()
    {
        var values = Protocol.Encode(2, 20);
        values[4] ^= 1; // flip a sequence bit, as if captured mid-redraw
        var r = PixelDecoder.Decode(Render(values, 4));
        Assert.False(r.Found);
    }

    [Fact]
    public void HintedBlockSizeIsUsed()
    {
        var img = Render(Protocol.Encode(3, 50), 7);
        var r = PixelDecoder.Decode(img, hintBlockSize: 7);
        Assert.True(r.Found);
        Assert.Equal(7, r.BlockSize);
        // A stale hint still falls back to measuring.
        Assert.True(PixelDecoder.Decode(img, hintBlockSize: 4).Found);
    }

    [Fact]
    public void ReturnsUsefulDetail_WhenOnlyMarkerVisible()
    {
        // Magenta in the corner but the rest covered by another window.
        var img = Render([Protocol.StartMarkerA], 4, seed: 3);
        var r = PixelDecoder.Decode(img);
        Assert.False(r.Found);
        Assert.NotEqual("no start marker in the top-left corner", r.Detail);
    }
}

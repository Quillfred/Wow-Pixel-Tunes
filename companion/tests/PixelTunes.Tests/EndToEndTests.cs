using PixelTunes.Core;
using Xunit;

namespace PixelTunes.Tests;

/// <summary>
/// Feeds frames produced by the real addon code (tools/simulate_addon.lua, recorded in
/// addon_vectors.txt) through the decoder and tracker, the same path the companion app uses.
/// </summary>
public class EndToEndTests
{
    private static List<(string Label, int[] Values)> LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "addon_vectors.txt");
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var parts = l.Split(' ');
                return (parts[0], parts[1].Split(',').Select(int.Parse).ToArray());
            })
            .ToList();
    }

    [Fact]
    public void AddonFramesMatchCSharpEncoder()
    {
        foreach (var (label, values) in LoadVectors())
        {
            int cmd = values[2], seq = values[3] * 8 + values[4];
            Assert.Equal(Protocol.Encode(cmd, seq), values);
            _ = label;
        }
    }

    [Fact]
    public void TrackerExecutesEachPressExactlyOnce()
    {
        var vectors = LoadVectors();
        var tracker = new CommandTracker(requiredStableReads: 2);
        var executed = new List<MediaCommand>();
        var expected = new List<MediaCommand>();
        int lastSeq = -1;

        foreach (var (label, values) in vectors)
        {
            var img = DecoderTests.Render(values, 4, seed: label.GetHashCode());
            int seq = values[3] * 8 + values[4];
            if (lastSeq >= 0 && seq != lastSeq) expected.Add((MediaCommand)values[2]);
            lastSeq = seq;

            // The companion samples ~20x/second, so each frame is seen several times.
            for (int i = 0; i < 3; i++)
            {
                var cmd = tracker.Process(PixelDecoder.Decode(img, 4));
                if (cmd != null) executed.Add(cmd.Value);
            }
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected, executed);
        Assert.Equal(MediaCommand.Next, executed[0]);
        Assert.Equal(MediaCommand.Next, executed[1]); // the queued second press was not lost
    }

    [Fact]
    public void FirstFrameOnlySetsBaseline_AndDisappearingResetsIt()
    {
        var tracker = new CommandTracker(2);
        var seen = PixelDecoder.Decode(DecoderTests.Render(Protocol.Encode(2, 5), 4));
        var gone = PixelDecoder.Decode(BgraImage.Create(160, 32));

        // Companion starts while WoW already shows "Next, seq 5": must not skip a track.
        Assert.Null(tracker.Process(seen));
        Assert.Null(tracker.Process(seen));
        Assert.Equal(5, tracker.BaselineSequence);

        // Alt-tab away and back: same frame again, still nothing executed.
        Assert.Null(tracker.Process(gone));
        Assert.Null(tracker.BaselineSequence);
        Assert.Null(tracker.Process(seen));
        Assert.Null(tracker.Process(seen));

        // A real press is executed once.
        var pressed = PixelDecoder.Decode(DecoderTests.Render(Protocol.Encode(3, 6), 4));
        Assert.Null(tracker.Process(pressed));            // first read: not yet stable
        Assert.Equal(MediaCommand.Previous, tracker.Process(pressed));
        Assert.Null(tracker.Process(pressed));
    }

    [Fact]
    public void SingleGlitchedReadIsIgnored()
    {
        var tracker = new CommandTracker(2);
        var idle = PixelDecoder.Decode(DecoderTests.Render(Protocol.Encode(0, 0), 4));
        tracker.Process(idle); tracker.Process(idle);

        // One read that decodes to a different (but valid) frame, then back to idle.
        var glitch = PixelDecoder.Decode(DecoderTests.Render(Protocol.Encode(2, 1), 4));
        Assert.Null(tracker.Process(glitch));
        Assert.Null(tracker.Process(idle));
        Assert.Null(tracker.Process(idle));
    }
}

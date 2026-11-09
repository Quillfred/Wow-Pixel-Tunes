namespace PixelTunes.Core;

/// <summary>
/// The pixel protocol shared with the WoW addon (see addon/PixelTunes/PixelTunes.lua).
///
/// The addon draws a horizontal strip of 7 square blocks in the top-left corner of the
/// WoW window. Each block is a solid color where every channel is either fully off (0)
/// or fully on (255), so each block carries 3 bits:  value = R*4 + G*2 + B.
///
///   block 0  start marker   = 5 (magenta)
///   block 1  start marker   = 3 (cyan)
///   block 2  command        (0 = none, 1 = play/pause, 2 = next, 3 = previous,
///                           4 = volume up, 5 = volume down)
///   block 3  sequence high  (seq >> 3)
///   block 4  sequence low   (seq &amp; 7)
///   block 5  checksum       (cmd + 3*seqHi + 5*seqLo + 2) mod 8
///   block 6  end marker     = 6 (yellow)
///
/// The sequence number (0-63) increases every time a button is pressed, so pressing
/// "Next" twice produces two distinct frames. The companion acts only when it sees the
/// sequence number change.
/// </summary>
public static class Protocol
{
    public const int BlockCount = 7;
    public const int StartMarkerA = 5;
    public const int StartMarkerB = 3;
    public const int EndMarker = 6;
    public const int SeqModulo = 64;
    public const int MaxCommand = (int)MediaCommand.VolumeDown;

    /// <summary>Largest block size (in physical pixels) the decoder will search for.</summary>
    public const int MaxBlockSize = 24;

    public static int Checksum(int cmd, int seqHi, int seqLo) => (cmd + 3 * seqHi + 5 * seqLo + 2) % 8;

    /// <summary>Encodes a frame as the 7 block values the addon would draw.</summary>
    public static int[] Encode(int cmd, int seq)
    {
        int hi = (seq >> 3) & 7, lo = seq & 7;
        return [StartMarkerA, StartMarkerB, cmd & 7, hi, lo, Checksum(cmd & 7, hi, lo), EndMarker];
    }

    /// <summary>Quantizes a captured color to a 3-bit value (threshold at the midpoint).</summary>
    public static int Quantize(byte r, byte g, byte b) =>
        (r >= 128 ? 4 : 0) | (g >= 128 ? 2 : 0) | (b >= 128 ? 1 : 0);

    public static (byte R, byte G, byte B) ColorOf(int value) =>
        ((value & 4) != 0 ? (byte)255 : (byte)0,
         (value & 2) != 0 ? (byte)255 : (byte)0,
         (value & 1) != 0 ? (byte)255 : (byte)0);
}

public enum MediaCommand
{
    None = 0,
    PlayPause = 1,
    Next = 2,
    Previous = 3,
    VolumeUp = 4,
    VolumeDown = 5,
}

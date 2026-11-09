namespace PixelTunes.Core;

/// <summary>
/// Turns a stream of decoded frames into commands to execute.
///
/// Rules:
///  * A frame must be read identically on <see cref="RequiredStableReads"/> consecutive
///    captures before it counts (guards against a capture taken mid-redraw).
///  * The first stable frame after the strip appears (companion start, /reload, alt-tab
///    back into WoW) only sets the baseline. Nothing is executed, so stale state never
///    replays a command.
///  * After that, a command is executed when the sequence number changes.
///  * When the strip disappears, the baseline is forgotten.
/// </summary>
public sealed class CommandTracker
{
    public int RequiredStableReads { get; }

    private int? _baselineSeq;
    private (MediaCommand Cmd, int Seq)? _pending;
    private int _pendingCount;

    public CommandTracker(int requiredStableReads = 2)
    {
        RequiredStableReads = Math.Max(1, requiredStableReads);
    }

    public int? BaselineSequence => _baselineSeq;

    /// <summary>Feeds one decode result; returns a command to execute, or null.</summary>
    public MediaCommand? Process(DecodeResult result)
    {
        if (!result.Found)
        {
            _baselineSeq = null;
            _pending = null;
            _pendingCount = 0;
            return null;
        }

        var frame = (result.Command, result.Sequence);
        if (_pending == frame) _pendingCount++;
        else { _pending = frame; _pendingCount = 1; }

        if (_pendingCount < RequiredStableReads) return null;

        if (_baselineSeq == null)
        {
            _baselineSeq = frame.Sequence;
            return null;
        }

        if (_baselineSeq == frame.Sequence) return null;

        _baselineSeq = frame.Sequence;
        return frame.Command == MediaCommand.None ? null : frame.Command;
    }
}

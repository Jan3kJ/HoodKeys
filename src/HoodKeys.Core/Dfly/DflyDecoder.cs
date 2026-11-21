namespace HoodKeys.Core.Dfly;

/// <summary>
/// Turns D-Fly reports into button events by comparing each report with the previous one.
/// </summary>
/// <remarks>
/// Byte 0 is a report counter (0x20 to 0x2F, wraps, may skip values); it carries no event and is
/// not interpreted. Bytes 1 to 4 are channels 1 to 4. A channel byte is 0xF0 until its first press
/// on the link; after that the high nibble is the event (1 short press, 2 long press started,
/// 3 still held, 4 quick double tap, 0 released after a long press) and the low nibble a
/// per-channel event counter.
/// Every channel starts at 0xF0, which is what a new link starts with, so the first report is
/// decoded like any other: a channel that goes from 0xF0 to a known nibble is one event, a channel
/// that stays at 0xF0 is none. <see cref="Reset"/> returns all channels to 0xF0, for a new link.
/// A channel byte that differs from the previous report's byte for that channel is exactly one
/// event; an unchanged byte is no event, so repeated reports are harmless. A change to any other
/// high nibble (5 to E, or F other than 0xF0) is not an event and is ignored.
/// Channels missing from a short report are left alone; bytes beyond the fourth channel are
/// ignored. The decoder never throws on payload content.
/// </remarks>
public sealed class DflyDecoder
{
    public const int ChannelCount = 4;

    private const byte Idle = 0xF0;

    private readonly byte[] _state = NewState();

    /// <summary>Returns all channels to 0xF0, the state of a new link.</summary>
    public void Reset() => Array.Fill(_state, Idle);

    public IReadOnlyList<Di2Event> Decode(byte[]? report)
    {
        var events = new List<Di2Event>();
        if (report is null)
        {
            return events;
        }

        var channels = Math.Min(report.Length - 1, ChannelCount);
        for (var i = 0; i < channels; i++)
        {
            var previous = _state[i];
            var current = report[i + 1];
            _state[i] = current;

            if (current == previous)
            {
                continue;
            }

            if (EventKind(current) is { } kind)
            {
                events.Add(new Di2Event(i + 1, kind));
            }
        }

        return events;
    }

    private static byte[] NewState()
    {
        var state = new byte[ChannelCount];
        Array.Fill(state, Idle);
        return state;
    }

    private static Di2EventKind? EventKind(byte value) => (value >> 4) switch
    {
        0x1 => Di2EventKind.ShortPress,
        0x2 => Di2EventKind.LongPress,
        0x3 => Di2EventKind.LongHold,
        0x4 => Di2EventKind.DoubleTap,
        0x0 => Di2EventKind.Release,
        _ => null,
    };
}

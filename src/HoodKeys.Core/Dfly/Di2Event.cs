namespace HoodKeys.Core.Dfly;

/// <summary>What happened to a D-Fly channel.</summary>
public enum Di2EventKind
{
    ShortPress,
    LongPress,
    LongHold,
    DoubleTap,
    Release,
}

/// <summary>A button event on one D-Fly channel (1 to 4).</summary>
public sealed record Di2Event(int Channel, Di2EventKind Kind)
{
    public override string ToString() => $"{Channel}:{Kind}";
}

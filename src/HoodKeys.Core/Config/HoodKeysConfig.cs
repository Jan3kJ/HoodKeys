using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Config;

/// <summary>The validated contents of a config file.</summary>
public sealed record HoodKeysConfig(DeviceConfig? Device, IReadOnlyDictionary<int, ChannelConfig> Channels);

/// <summary>The Di2 unit to connect to, by name or Bluetooth address; both null means any Di2 found.</summary>
public sealed record DeviceConfig(string? Name, string? Address);

/// <summary>What one channel does on a short press and on a long press.</summary>
public sealed record ChannelConfig(ActionConfig? Short, LongPressConfig? Long);

/// <summary>Send a key or combo (<see cref="Keys"/> set), or run a PowerShell command (<see cref="Command"/> set).</summary>
public sealed record ActionConfig(KeyCombo? Keys, string? Command)
{
    /// <summary>The key text as written in the file, for display.</summary>
    public string? KeyText { get; init; }
}

/// <summary>A long press is its own action, or repeats the short action every <see cref="RepeatIntervalMs"/> while held.</summary>
public sealed record LongPressConfig(ActionConfig? Action, int? RepeatIntervalMs)
{
    public bool IsRepeat => Action is null;
}

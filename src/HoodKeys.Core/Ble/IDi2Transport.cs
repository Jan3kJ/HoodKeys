namespace HoodKeys.Core.Ble;

/// <summary>Bluetooth LE access to Di2 units; the WinRT implementation is <see cref="WinRtDi2Transport"/>.</summary>
public interface IDi2Transport
{
    /// <summary>Watches advertisements for <paramref name="duration"/> and returns every device seen.</summary>
    Task<IReadOnlyList<AdvertisedDevice>> ScanAsync(TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>Opens a link to the device; GATT traffic starts with the first call on the link.</summary>
    Task<IDi2Link> ConnectAsync(ulong address, CancellationToken cancellationToken);
}

/// <summary>An open link to one Di2 unit.</summary>
public interface IDi2Link : IDisposable
{
    string? Name { get; }

    bool IsConnected { get; }

    /// <summary>Raised with the new state when the link connects or drops.</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>Raised for every value the D-Fly characteristic delivers, read or indicated.</summary>
    event Action<RawReport>? ReportReceived;

    /// <summary>Lists all services and their characteristics, bypassing the Windows GATT cache.</summary>
    Task<IReadOnlyList<GattServiceInfo>> DiscoverAsync(CancellationToken cancellationToken);

    /// <summary>Reads the D-Fly characteristic once; null when it is missing or not readable.</summary>
    Task<RawReport?> ReadDflyAsync(CancellationToken cancellationToken);

    /// <summary>Subscribes to D-Fly indications (notifications if the unit offers only those) and returns which one.</summary>
    Task<string> SubscribeDflyAsync(CancellationToken cancellationToken);
}

public sealed record GattServiceInfo(Guid Uuid, IReadOnlyList<GattCharacteristicInfo> Characteristics);

public sealed record GattCharacteristicInfo(Guid Uuid, string Properties);

/// <summary>One raw value from the D-Fly characteristic. Source is <c>read</c>, <c>indicate</c> or <c>notify</c>.</summary>
public sealed record RawReport(DateTimeOffset Timestamp, string Source, byte[] Data)
{
    /// <summary>Space-separated upper-case hex, the format of the decoder fixtures.</summary>
    public string Hex => string.Join(' ', Data.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
}

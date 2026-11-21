namespace HoodKeys.Core.Ble;

/// <summary>A device seen during a scan, merged over all its advertisements.</summary>
public sealed record AdvertisedDevice(ulong Address, string? Name, short Rssi, IReadOnlyList<Guid> ServiceUuids)
{
    public bool AdvertisesDfly => ServiceUuids.Contains(DflyGatt.ServiceUuid);

    public bool LooksLikeDi2 => AdvertisesDfly || Di2Names.LooksShimano(Name);
}

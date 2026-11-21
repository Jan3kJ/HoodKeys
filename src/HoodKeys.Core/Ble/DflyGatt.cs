namespace HoodKeys.Core.Ble;

/// <summary>GATT identifiers of the Di2 D-Fly interface.</summary>
public static class DflyGatt
{
    public static readonly Guid ServiceUuid = new("000018ef-5348-494d-414e-4f5f424c4500");

    /// <summary>Carries D-Fly reports by indication; may also be readable.</summary>
    public static readonly Guid CharacteristicUuid = new("00002ac2-5348-494d-414e-4f5f424c4500");
}

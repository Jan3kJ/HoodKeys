namespace HoodKeys.Core.Ble;

/// <summary>Name heuristic for Shimano units, used only to highlight scan results.</summary>
public static class Di2Names
{
    // Shimano brand words and component model prefixes (rear derailleur, wireless unit, shifters, GRX).
    private static readonly string[] Markers = ["SHIMANO", "DI2", "RD-", "EW-", "ST-", "SW-", "RX8", "GRX"];

    public static bool LooksShimano(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Markers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));
}

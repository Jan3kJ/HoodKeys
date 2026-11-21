using System.Globalization;

namespace HoodKeys.Core.Ble;

/// <summary>48-bit Bluetooth addresses as text: <c>AA:BB:CC:DD:EE:FF</c>.</summary>
public static class BleAddress
{
    public static string Format(ulong address) =>
        string.Join(':', Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>Accepts six hex pairs separated by <c>:</c> or <c>-</c>, or 12 hex digits without separators.</summary>
    public static bool TryParse(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        var separated = hex.Length != text.Trim().Length;
        if (hex.Length != 12 || (separated && text.Trim().Length != 17))
        {
            return false;
        }

        return ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }
}

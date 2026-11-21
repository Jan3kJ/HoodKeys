using HoodKeys.Core.Ble;
using HoodKeys.Core.Config;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Cli.Commands;

public static class DeviceSelector
{
    /// <summary>
    /// The session target: <paramref name="option"/> (<c>--device</c>) first, then the config's device
    /// (address before name), else the strongest Di2 a scan finds. Null when the scan finds none.
    /// </summary>
    public static async Task<string?> ResolveAsync(
        string? option,
        DeviceConfig? configured,
        IDi2Transport transport,
        TimeSpan scan,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(option))
        {
            return option;
        }

        var fromConfig = configured?.Address ?? configured?.Name;
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        logger.LogInformation("No device given; scanning {Seconds:0} s for a Di2", scan.TotalSeconds);
        var devices = await transport.ScanAsync(scan, cancellationToken);
        var best = devices.Where(d => d.LooksLikeDi2).OrderByDescending(d => d.Rssi).FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        logger.LogInformation("Using {Name} {Address}", best.Name ?? "(no name)", BleAddress.Format(best.Address));
        return BleAddress.Format(best.Address);
    }
}

using System.CommandLine;
using System.Globalization;
using HoodKeys.Core.Ble;
using HoodKeys.Core.Dfly;

namespace HoodKeys.Cli.Spike;

/// <summary>Hidden diagnostic commands: raw advertisement scan and raw D-Fly monitor.</summary>
internal static class SpikeCommands
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    public static IEnumerable<Command> Create(IDi2Transport transport)
    {
        var scan = new Command("spike-scan", "Watch BLE advertisements for 10 s and list every device") { Hidden = true };
        scan.SetAction((_, ct) => ScanAsync(transport, ct));

        var address = new Argument<string>("address") { Description = "Bluetooth address, e.g. AA:BB:CC:DD:EE:FF" };
        var monitor = new Command("spike-monitor", "Connect, list GATT services and log raw D-Fly payloads") { Hidden = true };
        monitor.Arguments.Add(address);
        monitor.SetAction((parse, ct) => MonitorAsync(transport, parse.GetValue(address), ct));

        return [scan, monitor];
    }

    private static async Task<int> ScanAsync(IDi2Transport transport, CancellationToken ct)
    {
        Console.WriteLine($"Scanning for {ScanDuration.TotalSeconds:0} s ...");
        IReadOnlyList<AdvertisedDevice> devices;
        try
        {
            devices = await transport.ScanAsync(ScanDuration, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Scan failed: {e.Message}");
            return 1;
        }

        Console.WriteLine($"{devices.Count} device(s). '*' = advertises D-Fly, '?' = Shimano-looking name.");
        foreach (var d in devices.OrderByDescending(d => d.LooksLikeDi2).ThenByDescending(d => d.Rssi))
        {
            var mark = d.AdvertisesDfly ? "*" : d.LooksLikeDi2 ? "?" : " ";
            var uuids = d.ServiceUuids.Count == 0 ? "-" : string.Join(", ", d.ServiceUuids);
            Console.WriteLine($"{mark} {BleAddress.Format(d.Address)}  {d.Rssi,4} dBm  {d.Name ?? "(no name)"}  services: {uuids}");
        }

        return 0;
    }

    private static async Task<int> MonitorAsync(IDi2Transport transport, string? addressText, CancellationToken ct)
    {
        if (!BleAddress.TryParse(addressText, out var address))
        {
            Console.Error.WriteLine($"Not a Bluetooth address: '{addressText}' (expected AA:BB:CC:DD:EE:FF)");
            return 2;
        }

        var logPath = Path.Combine(AppContext.BaseDirectory, "spike-monitor.log");
        using var log = new SpikeLog(logPath);
        log.Line($"spike-monitor {BleAddress.Format(address)}, log {logPath}");

        var decoder = new DflyDecoder();
        try
        {
            using var link = await transport.ConnectAsync(address, ct);
            link.ConnectionChanged += connected => log.Line(connected ? "connected" : "disconnected");
            link.ReportReceived += report =>
            {
                var events = decoder.Decode(report.Data);
                var decoded = events.Count == 0 ? "" : "  => " + string.Join(' ', events);
                log.Line($"{report.Source,-8} {report.Hex}{decoded}", report.Timestamp);
            };

            log.Line($"device name: {link.Name ?? "(none)"}");
            var services = await link.DiscoverAsync(ct);
            foreach (var s in services)
            {
                log.Line($"service {s.Uuid}{(s.Uuid == DflyGatt.ServiceUuid ? "  (D-Fly)" : "")}");
                foreach (var c in s.Characteristics)
                {
                    log.Line($"  characteristic {c.Uuid}  [{c.Properties}]{(c.Uuid == DflyGatt.CharacteristicUuid ? "  (D-Fly)" : "")}");
                }
            }

            if (await link.ReadDflyAsync(ct) is null)
            {
                log.Line("D-Fly characteristic not readable (or missing); skipped read");
            }

            var mode = await link.SubscribeDflyAsync(ct);
            log.Line($"subscribed ({mode}); press the D-Fly buttons, Ctrl+C to stop");
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            log.Line("stopped");
            return 0;
        }
        catch (Exception e)
        {
            log.Line($"error: {e.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>Writes each line to the console and appends it, timestamped, to the log file.</summary>
    private sealed class SpikeLog(string path) : IDisposable
    {
        private readonly StreamWriter _writer = new(path, append: true) { AutoFlush = true };

        public void Line(string text, DateTimeOffset? at = null)
        {
            var line = $"{(at ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {text}";
            lock (_writer)
            {
                Console.WriteLine(line);
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_writer)
            {
                _writer.Dispose();
            }
        }
    }
}

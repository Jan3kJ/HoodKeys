using System.CommandLine;
using System.Globalization;
using HoodKeys.Cli.Logging;
using HoodKeys.Core.Actions;
using HoodKeys.Core.Ble;
using HoodKeys.Core.Config;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Cli.Commands;

/// <summary>The user-facing commands: <c>scan</c>, <c>monitor</c> and <c>run</c>.</summary>
public static class CliCommands
{
    public const int DefaultScanSeconds = 10;

    public static readonly Option<bool> Verbose = new("--verbose")
    {
        Description = "Show Debug entries (raw reports) on the console too; the log file always has them",
        Recursive = true,
    };

    public static IEnumerable<Command> Create(CliServices services)
    {
        var seconds = new Option<int>("--seconds") { Description = "How long to scan", DefaultValueFactory = _ => DefaultScanSeconds };
        seconds.Validators.Add(r =>
        {
            if (r.Tokens.Count > 0 && !(int.TryParse(r.Tokens[0].Value, CultureInfo.InvariantCulture, out var value) && value >= 1))
            {
                r.AddError("--seconds must be at least 1");
            }
        });
        var scan = new Command("scan", "List the Di2 units in range") { seconds };
        scan.SetAction((parse, ct) => ScanAsync(services, parse.GetValue(seconds), parse.GetValue(Verbose), ct));

        var monitorDevice = DeviceOption();
        var monitor = new Command("monitor", "Print every button event live until Ctrl+C") { monitorDevice };
        monitor.SetAction((parse, ct) => MonitorAsync(services, parse.GetValue(monitorDevice), parse.GetValue(Verbose), ct));

        var config = new Option<string?>("--config") { Description = "Config file (default: %APPDATA%\\HoodKeys\\config.json)" };
        var runDevice = DeviceOption();
        var run = new Command("run", "Turn button events into keys and commands from the config until Ctrl+C") { config, runDevice };
        run.SetAction((parse, ct) => RunAsync(services, parse.GetValue(config), parse.GetValue(runDevice), parse.GetValue(Verbose), ct));

        return [scan, monitor, run];
    }

    /// <summary>The config file to load: <c>--config</c> when given, else the default path.</summary>
    public static string ResolveConfigPath(string? option) =>
        string.IsNullOrWhiteSpace(option) ? ConfigPaths.DefaultPath() : Path.GetFullPath(option);

    /// <summary><c>HH:mm:ss.fff  channel N  Kind  hex</c>.</summary>
    public static string FormatEvent(Di2Notification n) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{n.Timestamp.ToLocalTime():HH:mm:ss.fff}  channel {n.Event.Channel}  {n.Event.Kind}  {n.Hex}");

    private static Option<string?> DeviceOption() =>
        new("--device") { Description = "Di2 address (AA:BB:CC:DD:EE:FF) or name prefix (default: the config's device, else the first Di2 found)" };

    private static CliLogging OpenLog(CliServices s, bool verbose) =>
        CliLogging.Create(s.LogDirectory ?? LogPaths.DefaultDirectory(), s.Out, verbose, s.Time);

    private static async Task<int> ScanAsync(CliServices s, int seconds, bool verbose, CancellationToken ct)
    {
        using var logging = OpenLog(s, verbose);
        var logger = logging.CreateLogger("Scan");
        logger.LogInformation("Scanning for {Seconds} s", seconds);
        IReadOnlyList<AdvertisedDevice> devices;
        try
        {
            devices = await s.Transport.ScanAsync(TimeSpan.FromSeconds(seconds), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Scan failed: {Message}", e.Message);
            return 1;
        }

        var ordered = devices.OrderByDescending(d => d.LooksLikeDi2).ThenByDescending(d => d.Rssi).ToList();
        var di2Count = ordered.Count(d => d.LooksLikeDi2);
        foreach (var d in ordered)
        {
            var mark = d.LooksLikeDi2 ? "Di2  " : "     ";
            await s.Out.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{mark}{d.Name ?? "(no name)",-24} {BleAddress.Format(d.Address)}  {d.Rssi,4} dBm"));
        }

        if (di2Count == 0)
        {
            logger.LogWarning("No Di2 found ({Count} other device(s) seen). Wake the bike and try again", ordered.Count);
            return 1;
        }

        logger.LogInformation("{Count} Di2 unit(s) found", di2Count);
        return 0;
    }

    private static async Task<int> MonitorAsync(CliServices s, string? device, bool verbose, CancellationToken ct)
    {
        using var logging = OpenLog(s, verbose);
        await s.Out.WriteLineAsync($"Log file: {logging.LogFile}");
        var target = await ResolveTargetAsync(s, device, null, logging, ct);
        if (target is null)
        {
            return 1;
        }

        var session = new Di2Session(s.Transport, target, logging.CreateLogger<Di2Session>(), s.Delay);
        var gate = new object();
        session.EventReceived += n =>
        {
            lock (gate)
            {
                s.Out.WriteLine(FormatEvent(n));
            }
        };

        await session.RunAsync(ct);
        return 0;
    }

    private static async Task<int> RunAsync(CliServices s, string? configOption, string? device, bool verbose, CancellationToken ct)
    {
        var loaded = ConfigLoader.Load(ResolveConfigPath(configOption));
        if (loaded.Config is not { } config)
        {
            foreach (var error in loaded.Errors)
            {
                await s.Error.WriteLineAsync(error.ToString());
            }

            return 1;
        }

        using var logging = OpenLog(s, verbose);
        await s.Out.WriteLineAsync($"Log file: {logging.LogFile}");
        var target = await ResolveTargetAsync(s, device, config.Device, logging, ct);
        if (target is null)
        {
            return 1;
        }

        var commands = s.Commands ?? new PowerShellRunner(logging.CreateLogger<PowerShellRunner>());
        using var dispatcher = new ActionDispatcher(config, s.Keys, commands, logging.CreateLogger<ActionDispatcher>(), s.Time);
        var session = new Di2Session(s.Transport, target, logging.CreateLogger<Di2Session>(), s.Delay);
        session.EventReceived += n => dispatcher.Handle(n.Event);
        session.LinkLost += dispatcher.StopAllRepeats;

        await session.RunAsync(ct);
        return 0;
    }

    private static async Task<string?> ResolveTargetAsync(CliServices s, string? device, DeviceConfig? configured, CliLogging logging, CancellationToken ct)
    {
        var logger = logging.CreateLogger("Device");
        try
        {
            var target = await DeviceSelector.ResolveAsync(device, configured, s.Transport, s.PickScan, logger, ct);
            if (target is null)
            {
                logger.LogError("No Di2 found. Wake the bike, or name the unit with --device");
            }

            return target;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Scan failed: {Message}", e.Message);
            return null;
        }
    }
}

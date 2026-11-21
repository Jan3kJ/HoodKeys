using System.CommandLine;
using HoodKeys.Cli.Commands;
using HoodKeys.Cli.Logging;
using HoodKeys.Core.Actions;
using HoodKeys.Core.Ble;
using HoodKeys.Core.Config;
using HoodKeys.Core.Dfly;
using HoodKeys.Core.Keys;
using HoodKeys.Core.Tests.Actions;
using HoodKeys.Core.Tests.Ble;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Core.Tests.Cli;

public sealed class CliCommandsTests : IDisposable
{
    private const string Address = "AA:BB:CC:DD:EE:01";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoodkeys-cli-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTransport _transport = new();
    private readonly FakeKeySender _keys = new();
    private readonly FakeCommandRunner _commands = new();
    private readonly FakeTimeProvider _time = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public CliCommandsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition not reached");
    }

    private static AdvertisedDevice Bike(string name, short rssi, ulong address = 1) =>
        new(address, name, rssi, [DflyGatt.ServiceUuid]);

    private RootCommand Root()
    {
        var services = new CliServices(_transport, _keys)
        {
            Commands = _commands,
            Time = _time,
            Out = _out,
            Error = _error,
            LogDirectory = Path.Combine(_dir, "logs"),
            Delay = (_, ct) => Task.Delay(Timeout.Infinite, ct),
        };
        var root = new RootCommand();
        root.Options.Add(CliCommands.Verbose);
        foreach (var command in CliCommands.Create(services))
        {
            root.Subcommands.Add(command);
        }

        return root;
    }

    private string WriteConfig(string json, string name = "config.json")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private string LogText() => File.ReadAllText(LogPaths.FileFor(Path.Combine(_dir, "logs"), _time.GetLocalNow()));

    [Fact]
    public void Root_has_scan_monitor_and_run()
    {
        Assert.Equal(["scan", "monitor", "run"], Root().Subcommands.Select(c => c.Name));
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan --seconds 3")]
    [InlineData("scan --seconds 3 --verbose")]
    [InlineData("monitor")]
    [InlineData("monitor --device RDRX827")]
    [InlineData("run")]
    [InlineData("run --config c.json --device AA:BB:CC:DD:EE:FF --verbose")]
    public void Valid_command_lines_parse(string line)
    {
        Assert.Empty(Root().Parse(line.Split(' ')).Errors);
    }

    [Theory]
    [InlineData("scan --seconds 0")]
    [InlineData("scan --seconds ten")]
    [InlineData("monitor --device")]
    [InlineData("run --config")]
    [InlineData("run --unknown")]
    public void Invalid_command_lines_are_errors(string line)
    {
        Assert.NotEmpty(Root().Parse(line.Split(' ')).Errors);
    }

    [Fact]
    public async Task Scan_uses_seconds_and_lists_Di2_first_exit_0()
    {
        _transport.Advertised = [new AdvertisedDevice(2, "Headphones", -40, []), Bike("RDRX827 1", -60, 3)];

        var code = await Root().Parse(["scan", "--seconds", "3"]).InvokeAsync();

        Assert.Equal(0, code);
        Assert.Equal(TimeSpan.FromSeconds(3), _transport.LastScanDuration);
        var lines = _out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.Contains("  INF  ", StringComparison.Ordinal))
            .ToArray();
        Assert.StartsWith("Di2", lines[0], StringComparison.Ordinal);
        Assert.Contains("RDRX827 1", lines[0], StringComparison.Ordinal);
        Assert.Contains("-60 dBm", lines[0], StringComparison.Ordinal);
        Assert.Contains("00:00:00:00:00:03", lines[0], StringComparison.Ordinal);
        Assert.Contains("Headphones", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_defaults_to_10_seconds_and_exits_1_without_Di2()
    {
        _transport.Advertised = [new AdvertisedDevice(2, "Headphones", -40, [])];

        var code = await Root().Parse(["scan"]).InvokeAsync();

        Assert.Equal(1, code);
        Assert.Equal(TimeSpan.FromSeconds(10), _transport.LastScanDuration);
    }

    [Fact]
    public void Config_path_is_the_default_or_the_option()
    {
        Assert.Equal(ConfigPaths.DefaultPath(), CliCommands.ResolveConfigPath(null));
        Assert.EndsWith(Path.Combine("HoodKeys", "config.json"), CliCommands.ResolveConfigPath(" "), StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath("mine.json"), CliCommands.ResolveConfigPath("mine.json"));
    }

    [Fact]
    public void Log_file_is_hoodkeys_date_log_in_the_given_directory()
    {
        var path = LogPaths.FileFor("logs", new DateTimeOffset(2026, 11, 21, 23, 59, 0, TimeSpan.FromHours(1)));

        Assert.Equal(Path.Combine("logs", "hoodkeys-2026-11-21.log"), path);
        Assert.EndsWith(Path.Combine("HoodKeys", "logs"), LogPaths.DefaultDirectory(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_with_a_bad_config_exits_non_zero_naming_file_and_field()
    {
        var path = WriteConfig("""{ "channels": { "5": { "short": { "key": "I" } } } }""");

        var code = await Root().Parse(["run", "--config", path]).InvokeAsync();

        Assert.NotEqual(0, code);
        Assert.Contains(path, _error.ToString(), StringComparison.Ordinal);
        Assert.Contains("channels", _error.ToString(), StringComparison.Ordinal);
        Assert.Empty(_transport.Advertised);
        Assert.Equal(0, _transport.ConnectCalls);
    }

    [Fact]
    public async Task Run_turns_an_event_into_the_configured_key_with_one_latency_line()
    {
        var path = WriteConfig("""{ "channels": { "1": { "short": { "key": "I" } } } }""");
        var link = new FakeLink();
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var run = Root().Parse(["run", "--config", path, "--device", Address]).InvokeAsync(cancellationToken: cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);
        link.Report(0x20, 0xF0, 0xF0, 0xF0, 0xF0);
        link.Report(0x21, 0x11, 0xF0, 0xF0, 0xF0);
        await WaitUntil(() => _keys.Sent.Count == 1);
        await cts.CancelAsync();

        Assert.Equal(0, await run);
        Assert.Equal(KeyNames.TryParse("I", out _)!.Key, _keys.Sent.Single().Key);
        Assert.Contains("Log file: " + LogPaths.FileFor(Path.Combine(_dir, "logs"), _time.GetLocalNow()), _out.ToString(), StringComparison.Ordinal);
        var latency = LogText().Split(Environment.NewLine).Where(l => l.Contains("latency_ms=", StringComparison.Ordinal)).ToList();
        Assert.Single(latency);
        Assert.Contains("1:ShortPress", latency[0], StringComparison.Ordinal);
        Assert.Contains("latency_ms=0.00", latency[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_stops_a_held_repeat_when_the_link_drops()
    {
        var path = WriteConfig("""{ "channels": { "1": { "short": { "key": "I" }, "long": "repeat", "repeatIntervalMs": 100 } } }""");
        var link = new FakeLink();
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var run = Root().Parse(["run", "--config", path, "--device", Address]).InvokeAsync(cancellationToken: cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);
        link.Report(0x01, 0x20, 0xF0, 0xF0, 0xF0);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        var held = _keys.Sent.Count;
        link.Drop();
        await WaitUntil(() => link.Disposed);
        _time.Advance(TimeSpan.FromSeconds(1));
        await cts.CancelAsync();

        Assert.Equal(0, await run);
        Assert.Equal(3, held);
        Assert.Equal(held, _keys.Sent.Count);
        Assert.Contains("Repeat stopped on channel 1 (link lost)", LogText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_without_a_device_uses_the_config_device_then_the_first_Di2()
    {
        var named = WriteConfig("""{ "device": { "address": "AA:BB:CC:DD:EE:02" }, "channels": { "1": { "short": { "key": "I" } } } }""", "named.json");
        var anonymous = WriteConfig("""{ "channels": { "1": { "short": { "key": "I" } } } }""", "anonymous.json");
        _transport.Advertised = [new AdvertisedDevice(0x2, "Headphones", -30, []), Bike("RDRX827", -70, 0xAABBCCDDEE03)];

        foreach (var (config, expected) in new[] { (named, 0xAABBCCDDEE02UL), (anonymous, 0xAABBCCDDEE03UL) })
        {
            var link = new FakeLink();
            _transport.Enqueue(link);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var run = Root().Parse(["run", "--config", config]).InvokeAsync(cancellationToken: cts.Token);
            await WaitUntil(() => link.Subscriptions == 1);
            await cts.CancelAsync();
            Assert.Equal(0, await run);
            Assert.Equal(expected, _transport.LastAddress);
        }
    }

    [Fact]
    public async Task Run_exits_1_when_no_Di2_is_found()
    {
        var path = WriteConfig("""{ "channels": { "1": { "short": { "key": "I" } } } }""");
        _transport.Advertised = [new AdvertisedDevice(2, "Headphones", -40, [])];

        Assert.Equal(1, await Root().Parse(["run", "--config", path]).InvokeAsync());
        Assert.Contains("No Di2 found", _out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Monitor_prints_every_event_with_channel_kind_and_hex()
    {
        var link = new FakeLink();
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var monitor = Root().Parse(["monitor", "--device", Address]).InvokeAsync(cancellationToken: cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);
        link.Report(0x20, 0xF0, 0xF0, 0xF0, 0xF0);
        link.Report(0x21, 0x11, 0xF0, 0xF0, 0xF0);
        link.Report(0x22, 0x11, 0x20, 0xF0, 0xF0);
        await WaitUntil(() => _out.ToString().Contains("LongPress", StringComparison.Ordinal));
        await cts.CancelAsync();

        Assert.Equal(0, await monitor);
        var events = _out.ToString().Split(Environment.NewLine).Where(l => l.Contains("channel", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, events.Count);
        Assert.EndsWith("channel 1  ShortPress  21 11 F0 F0 F0", events[0], StringComparison.Ordinal);
        Assert.EndsWith("channel 2  LongPress  22 11 20 F0 F0", events[1], StringComparison.Ordinal);
        Assert.Contains("Log file:", _out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Event_line_starts_with_a_millisecond_time()
    {
        var at = new DateTimeOffset(new DateTime(2026, 11, 21, 9, 5, 7, 42, DateTimeKind.Local));

        var line = CliCommands.FormatEvent(new Di2Notification(new Di2Event(3, Di2EventKind.Release), at, "20 F0 F0 03 F0"));

        Assert.Equal("09:05:07.042  channel 3  Release  20 F0 F0 03 F0", line);
    }

    [Fact]
    public async Task Verbose_shows_Debug_entries_on_the_console_but_the_file_always_has_them()
    {
        var link = new FakeLink();
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var monitor = Root().Parse(["monitor", "--device", Address]).InvokeAsync(cancellationToken: cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);
        link.Report(0x20, 0xF0, 0xF0, 0xF0, 0xF0);
        await cts.CancelAsync();
        await monitor;

        Assert.DoesNotContain("DBG", _out.ToString(), StringComparison.Ordinal);
        Assert.Contains("DBG  Di2Session: Report indicate 20 F0 F0 F0 F0", LogText(), StringComparison.Ordinal);

        var verboseOut = new StringWriter();
        using var logging = CliLogging.Create(Path.Combine(_dir, "v"), verboseOut, verbose: true, _time);
        logging.CreateLogger("X").LogDebug("hello");
        Assert.Contains("DBG  X: hello", verboseOut.ToString(), StringComparison.Ordinal);
    }
}

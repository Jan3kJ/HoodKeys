using HoodKeys.Core.Dfly;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Core.Ble;

/// <summary>A decoded button event with the timestamp and hex of the report that carried it.</summary>
public sealed record Di2Notification(Di2Event Event, DateTimeOffset Timestamp, string Hex);

/// <summary>
/// Keeps one Di2 unit connected and turns its D-Fly reports into button events.
/// </summary>
/// <remarks>
/// The unit is given as an address or, when the target does not parse as one, a device name prefix
/// that is looked up by scan on every attempt. A failed attempt or a dropped link is retried after
/// a delay that starts at <see cref="InitialBackoff"/>, doubles up to <see cref="MaxBackoff"/> and
/// starts over once a link has subscribed. Every new link resets the decoder to all channels at 0xF0, so the
/// first report is decoded like any other.
/// </remarks>
public sealed class Di2Session
{
    public static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(4);

    private readonly IDi2Transport _transport;
    private readonly string _target;
    private readonly ILogger<Di2Session> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly DflyDecoder _decoder = new();
    private readonly object _gate = new();
    private bool _firstReportPending;

    /// <param name="target">Device address (<c>AA:BB:CC:DD:EE:FF</c>) or name prefix such as <c>RDRX827</c>.</param>
    /// <param name="delay">Waits between attempts; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public Di2Session(
        IDi2Transport transport,
        string target,
        ILogger<Di2Session> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _transport = transport;
        _target = target;
        _logger = logger;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Raised on the thread that delivers the report, once per decoded event.</summary>
    public event Action<Di2Notification>? EventReceived;

    /// <summary>Raised once when a subscribed link drops, or when the run is cancelled while a link is up; no release can follow then.</summary>
    public event Action? LinkLost;

    /// <summary>Connects and keeps reconnecting until <paramref name="cancellationToken"/> is cancelled; returns normally then.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = InitialBackoff;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subscribed = false;
                try
                {
                    subscribed = await RunLinkAsync(cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning(e, "Connection attempt failed: {Message}", e.Message);
                }

                if (subscribed)
                {
                    backoff = InitialBackoff;
                }

                _logger.LogInformation("Retrying in {Delay}", backoff);
                await _delay(backoff, cancellationToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Runs one link until it drops; returns whether it got as far as subscribing.</summary>
    private async Task<bool> RunLinkAsync(CancellationToken cancellationToken)
    {
        var address = await ResolveAddressAsync(cancellationToken);
        _logger.LogInformation("Connecting to {Address}", BleAddress.Format(address));
        using var link = await _transport.ConnectAsync(address, cancellationToken);
        _logger.LogInformation("Connected to {Name}", link.Name ?? "(unnamed)");

        var dropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnection(bool connected)
        {
            if (!connected)
            {
                dropped.TrySetResult();
            }
        }

        var subscribed = false;
        link.ConnectionChanged += OnConnection;
        link.ReportReceived += OnReport;
        try
        {
            lock (_gate)
            {
                _decoder.Reset();
                _firstReportPending = true;
            }

            var mode = await link.SubscribeDflyAsync(cancellationToken);
            subscribed = true;
            _logger.LogInformation("Subscribed ({Mode})", mode);

            await dropped.Task.WaitAsync(cancellationToken);
            _logger.LogInformation("Disconnected from {Name}", link.Name ?? "(unnamed)");
            return true;
        }
        finally
        {
            link.ConnectionChanged -= OnConnection;
            link.ReportReceived -= OnReport;
            if (subscribed)
            {
                LinkLost?.Invoke();
            }
        }
    }

    private async Task<ulong> ResolveAddressAsync(CancellationToken cancellationToken)
    {
        if (BleAddress.TryParse(_target, out var address))
        {
            return address;
        }

        _logger.LogInformation("Scanning for a device named {Prefix}*", _target);
        var devices = await _transport.ScanAsync(ScanDuration, cancellationToken);
        var match = devices
            .Where(d => d.Name?.StartsWith(_target, StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(d => d.Rssi)
            .FirstOrDefault();
        return match?.Address ?? throw new InvalidOperationException($"No device named {_target}* in range");
    }

    private void OnReport(RawReport report)
    {
        _logger.LogDebug("Report {Source} {Hex}", report.Source, report.Hex);

        IReadOnlyList<Di2Event> events;
        lock (_gate)
        {
            if (_firstReportPending)
            {
                _firstReportPending = false;
                _logger.LogInformation("First report after connect (decoded like any other): {Hex}", report.Hex);
            }

            events = _decoder.Decode(report.Data);
        }

        foreach (var e in events)
        {
            EventReceived?.Invoke(new Di2Notification(e, report.Timestamp, report.Hex));
        }
    }
}

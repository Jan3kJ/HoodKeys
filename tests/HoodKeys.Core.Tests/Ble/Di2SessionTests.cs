using HoodKeys.Core.Ble;
using HoodKeys.Core.Dfly;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Core.Tests.Ble;

internal sealed class FakeLink : IDi2Link
{
    public string? Name { get; init; } = "RDRX827";

    public bool IsConnected { get; private set; } = true;

    public int Subscriptions { get; private set; }

    public bool Disposed { get; private set; }

    public event Action<bool>? ConnectionChanged;

    public event Action<RawReport>? ReportReceived;

    public void Report(params byte[] data) =>
        ReportReceived?.Invoke(new RawReport(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero), "indicate", data));

    public void Drop()
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(false);
    }

    public Task<IReadOnlyList<GattServiceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GattServiceInfo>>([]);

    public Task<RawReport?> ReadDflyAsync(CancellationToken cancellationToken) => Task.FromResult<RawReport?>(null);

    public Task<string> SubscribeDflyAsync(CancellationToken cancellationToken)
    {
        Subscriptions++;
        return Task.FromResult("indicate");
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeTransport : IDi2Transport
{
    private readonly Queue<object> _outcomes = new();

    public int ConnectCalls { get; private set; }

    public ulong? LastAddress { get; private set; }

    public IReadOnlyList<AdvertisedDevice> Advertised { get; set; } = [];

    /// <summary>Queues the result of the next connect: a <see cref="FakeLink"/> or an <see cref="Exception"/>.</summary>
    public void Enqueue(object outcome) => _outcomes.Enqueue(outcome);

    public TimeSpan? LastScanDuration { get; private set; }

    public Task<IReadOnlyList<AdvertisedDevice>> ScanAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        LastScanDuration = duration;
        return Task.FromResult(Advertised);
    }

    public Task<IDi2Link> ConnectAsync(ulong address, CancellationToken cancellationToken)
    {
        ConnectCalls++;
        LastAddress = address;
        return _outcomes.Dequeue() switch
        {
            Exception e => Task.FromException<IDi2Link>(e),
            FakeLink link => Task.FromResult<IDi2Link>(link),
            _ => throw new InvalidOperationException("Unexpected outcome"),
        };
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    public bool Has(LogLevel level, string text) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(text, StringComparison.Ordinal));
}

public class Di2SessionTests
{
    private const string Address = "AA:BB:CC:DD:EE:FF";

    private readonly FakeTransport _transport = new();
    private readonly ListLogger<Di2Session> _log = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly List<Di2Notification> _events = [];

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition not reached");
    }

    private Task RecordDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        lock (_delays)
        {
            _delays.Add(delay);
        }

        return Task.CompletedTask;
    }

    private Di2Session Session(string target = Address, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var session = new Di2Session(_transport, target, _log, delay ?? RecordDelay);
        session.EventReceived += _events.Add;
        return session;
    }

    [Fact]
    public async Task Reports_become_events_with_the_report_timestamp()
    {
        var link = new FakeLink();
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource();
        var run = Session().RunAsync(cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);

        link.Report(0x20, 0xF0, 0xF0, 0xF0, 0xF0);
        link.Report(0x21, 0x11, 0xF0, 0xF0, 0xF0);

        var n = Assert.Single(_events);
        Assert.Equal(new Di2Event(1, Di2EventKind.ShortPress), n.Event);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero), n.Timestamp);
        Assert.Equal(0xAABBCCDDEEFFUL, _transport.LastAddress);
        Assert.True(_log.Has(LogLevel.Information, "Subscribed (indicate)"));
        Assert.True(_log.Has(LogLevel.Information, "First report after connect (decoded like any other): 20 F0 F0 F0 F0"));
        Assert.True(_log.Has(LogLevel.Debug, "21 11 F0 F0 F0"));

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Finds_the_device_by_name_prefix()
    {
        var link = new FakeLink();
        _transport.Advertised =
        [
            new AdvertisedDevice(1, "Other", -40, []),
            new AdvertisedDevice(2, "RDRX827-abc", -70, []),
        ];
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource();
        var run = Session("RDRX827").RunAsync(cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);

        Assert.Equal(2UL, _transport.LastAddress);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Reconnect_resubscribes_and_restarts_the_decoder_at_idle()
    {
        var first = new FakeLink();
        var second = new FakeLink();
        _transport.Enqueue(first);
        _transport.Enqueue(second);
        using var cts = new CancellationTokenSource();
        var run = Session().RunAsync(cts.Token);
        await WaitUntil(() => first.Subscriptions == 1);

        first.Report(0x20, 0x11, 0xF0, 0xF0, 0xF0);
        first.Drop();
        await WaitUntil(() => second.Subscriptions == 1);

        // Same byte as before the drop: the new link started at F0, so it is a new press.
        Assert.Single(_events);
        second.Report(0x22, 0x11, 0xF0, 0xF0, 0xF0);
        Assert.Equal(2, _events.Count);
        second.Report(0x23, 0x12, 0xF0, 0xF0, 0xF0);
        Assert.Equal(3, _events.Count);

        Assert.True(first.Disposed);
        Assert.Equal(Di2Session.InitialBackoff, _delays[0]);
        Assert.True(_log.Has(LogLevel.Information, "Disconnected"));
        Assert.True(_log.Has(LogLevel.Information, "Retrying in 00:00:01"));
        Assert.Equal(2, _log.Entries.Count(e => e.Message.StartsWith("First report after connect", StringComparison.Ordinal)));

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Connect_failures_back_off_to_the_cap()
    {
        for (var i = 0; i < 6; i++)
        {
            _transport.Enqueue(new InvalidOperationException("no unit"));
        }

        using var cts = new CancellationTokenSource();
        await Session(delay: (d, _) =>
        {
            _delays.Add(d);
            if (_delays.Count == 6)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        }).RunAsync(cts.Token);

        Assert.Equal(6, _transport.ConnectCalls);
        Assert.Equal([1, 2, 4, 5, 5, 5], _delays.Select(d => (int)d.TotalSeconds));
        Assert.True(_log.Has(LogLevel.Warning, "no unit"));
    }

    [Fact]
    public async Task A_dropped_link_raises_LinkLost_once()
    {
        var link = new FakeLink();
        _transport.Enqueue(link);
        var lost = 0;
        using var cts = new CancellationTokenSource();
        var session = Session();
        session.LinkLost += () => Interlocked.Increment(ref lost);
        var run = session.RunAsync(cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);

        link.Drop();
        await WaitUntil(() => link.Disposed);
        await cts.CancelAsync();
        await run;

        Assert.Equal(1, lost);
    }

    [Fact]
    public async Task Cancellation_with_a_link_up_raises_LinkLost()
    {
        var link = new FakeLink();
        _transport.Enqueue(link);
        var lost = 0;
        using var cts = new CancellationTokenSource();
        var session = Session();
        session.LinkLost += () => Interlocked.Increment(ref lost);
        var run = session.RunAsync(cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);

        await cts.CancelAsync();
        await run;

        Assert.Equal(1, lost);
    }

    [Fact]
    public async Task Backoff_restarts_after_a_subscribed_link_drops()
    {
        _transport.Enqueue(new InvalidOperationException("no unit"));
        _transport.Enqueue(new InvalidOperationException("no unit"));
        var link = new FakeLink();
        _transport.Enqueue(link);
        _transport.Enqueue(new InvalidOperationException("no unit"));
        using var cts = new CancellationTokenSource();
        var run = Session(delay: (d, _) =>
        {
            _delays.Add(d);
            if (_delays.Count == 4)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        }).RunAsync(cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);
        link.Drop();
        await run;

        Assert.Equal([1, 2, 1, 2], _delays.Select(d => (int)d.TotalSeconds));
    }

    [Fact]
    public async Task Cancellation_while_connected_stops_cleanly()
    {
        var link = new FakeLink();
        _transport.Enqueue(link);
        using var cts = new CancellationTokenSource();
        var run = Session().RunAsync(cts.Token);
        await WaitUntil(() => link.Subscriptions == 1);

        await cts.CancelAsync();
        await run;

        Assert.True(link.Disposed);
        Assert.Equal(1, _transport.ConnectCalls);
    }

    [Fact]
    public async Task Cancellation_during_backoff_stops_cleanly()
    {
        _transport.Enqueue(new InvalidOperationException("no unit"));
        using var cts = new CancellationTokenSource();
        var run = Session(delay: (_, ct) => Task.Delay(Timeout.Infinite, ct)).RunAsync(cts.Token);
        await WaitUntil(() => _log.Has(LogLevel.Information, "Retrying in"));

        await cts.CancelAsync();
        await run;

        Assert.Equal(1, _transport.ConnectCalls);
    }
}

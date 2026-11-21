using HoodKeys.Core.Actions;
using HoodKeys.Core.Config;
using HoodKeys.Core.Keys;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Core.Tests.Actions;

internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly List<FakeTimer> _timers = [];
    private TimeSpan _now = TimeSpan.Zero;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.Ticks;

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, _now + dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward, firing due timers in time order.</summary>
    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (true)
        {
            var next = _timers.Where(t => t.Next <= target).OrderBy(t => t.Next).FirstOrDefault();
            if (next is null)
            {
                break;
            }

            _now = next.Next;
            next.Fire();
        }

        _now = target;
    }

    private void Remove(FakeTimer timer) => _timers.Remove(timer);

    private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan next, TimeSpan period) : ITimer
    {
        public TimeSpan Next { get; private set; } = next;

        public void Fire()
        {
            Next += period;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod) => throw new NotSupportedException();

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeKeySender : IKeySender
{
    public List<KeyCombo> Sent { get; } = [];

    public void Send(KeyCombo combo) => Sent.Add(combo);
}

internal sealed class FakeCommandRunner : ICommandRunner
{
    public List<string> Started { get; } = [];

    /// <summary>Stands in for a slow command: the task stays pending, Start returns at once.</summary>
    public Task<int> Start(string command)
    {
        Started.Add(command);
        return new TaskCompletionSource<int>().Task;
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}

internal static class ConfigBuilder
{
    public static ActionConfig Key(string text) =>
        new(KeyNames.TryParse(text, out _), null) { KeyText = text };

    public static ActionConfig Command(string text) => new(null, text);

    public static HoodKeysConfig Of(params (int Channel, ChannelConfig Config)[] channels) =>
        new(null, channels.ToDictionary(c => c.Channel, c => c.Config));
}

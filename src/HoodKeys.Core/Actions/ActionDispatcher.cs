using System.Globalization;
using HoodKeys.Core.Config;
using HoodKeys.Core.Dfly;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Core.Actions;

/// <summary>
/// Maps <see cref="Di2Event"/>s to the actions in the config. A short press runs the channel's short action; a double tap runs it twice.
/// A long press runs its own action once, or in repeat mode starts repeating the short action every
/// <c>repeatIntervalMs</c> (first repeat one interval after the long press) until the release.
/// Only keys and commands written in the config are ever sent or run.
/// </summary>
public sealed class ActionDispatcher : IDisposable
{
    private readonly HoodKeysConfig _config;
    private readonly IKeySender _keys;
    private readonly ICommandRunner _commands;
    private readonly ILogger<ActionDispatcher> _logger;
    private readonly TimeProvider _time;
    private readonly Dictionary<int, ITimer> _repeats = [];
    private readonly object _gate = new();

    public ActionDispatcher(
        HoodKeysConfig config,
        IKeySender keys,
        ICommandRunner commands,
        ILogger<ActionDispatcher> logger,
        TimeProvider? time = null)
    {
        _config = config;
        _keys = keys;
        _commands = commands;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public void Handle(Di2Event e)
    {
        var received = Stamp();
        _logger.LogInformation("Event received {Event} at {ReceivedAt:O}", e, received.At);

        if (!_config.Channels.TryGetValue(e.Channel, out var channel))
        {
            _logger.LogDebug("No action for channel {Channel}", e.Channel);
            return;
        }

        switch (e.Kind)
        {
            case Di2EventKind.ShortPress:
                Run(e, channel.Short, received);
                break;
            case Di2EventKind.DoubleTap:
                Run(e, channel.Short, received);
                Run(e, channel.Short, received);
                break;
            case Di2EventKind.LongPress:
                HandleLong(e, channel, received);
                break;
            case Di2EventKind.Release:
                StopRepeat(e.Channel);
                break;
            case Di2EventKind.LongHold:
                // The device repeats "still held" about once a second; the dispatcher keeps its own repeat timer.
                break;
        }
    }

    /// <summary>Stops every running repeat; for a lost link, where no release will arrive.</summary>
    public void StopAllRepeats()
    {
        lock (_gate)
        {
            foreach (var channel in _repeats.Keys.ToList())
            {
                StopRepeatLocked(channel);
                _logger.LogInformation("Repeat stopped on channel {Channel} (link lost)", channel);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var timer in _repeats.Values)
            {
                timer.Dispose();
            }

            _repeats.Clear();
        }
    }

    private void HandleLong(Di2Event e, ChannelConfig channel, Received received)
    {
        if (channel.Long is not { } longPress)
        {
            return;
        }

        if (!longPress.IsRepeat)
        {
            Run(e, longPress.Action, received);
            return;
        }

        if (channel.Short is not { } action || longPress.RepeatIntervalMs is not { } intervalMs)
        {
            return;
        }

        var interval = TimeSpan.FromMilliseconds(intervalMs);
        lock (_gate)
        {
            StopRepeatLocked(e.Channel);
            _repeats[e.Channel] = _time.CreateTimer(_ => Run(e, action, Stamp()), null, interval, interval);
        }

        _logger.LogInformation("Repeat started on channel {Channel} every {IntervalMs} ms", e.Channel, intervalMs);
    }

    private void StopRepeat(int channel)
    {
        lock (_gate)
        {
            if (StopRepeatLocked(channel))
            {
                _logger.LogInformation("Repeat stopped on channel {Channel}", channel);
            }
        }
    }

    private bool StopRepeatLocked(int channel)
    {
        if (!_repeats.Remove(channel, out var timer))
        {
            return false;
        }

        timer.Dispose();
        return true;
    }

    private void Run(Di2Event e, ActionConfig? action, Received received)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            if (action.Keys is { } combo)
            {
                _keys.Send(combo);
                LogSent(e, $"key {action.KeyText ?? "combo"}", received);
            }
            else if (!string.IsNullOrWhiteSpace(action.Command))
            {
                _ = _commands.Start(action.Command);
                LogSent(e, "command", received);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action for {Event} failed", e);
        }
    }

    private Received Stamp() => new(_time.GetTimestamp(), _time.GetUtcNow());

    /// <summary>One line per sent action; <c>latency_ms=</c> is the figure for the latency check and easy to grep.</summary>
    private void LogSent(Di2Event e, string what, Received received) =>
        _logger.LogInformation(
            "Action sent for {Event}: {Action} received_at={ReceivedAt:O} sent_at={SentAt:O} latency_ms={LatencyMs}",
            e,
            what,
            received.At,
            _time.GetUtcNow(),
            _time.GetElapsedTime(received.Timestamp).TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));

    private readonly record struct Received(long Timestamp, DateTimeOffset At);
}

using System.Diagnostics;
using HoodKeys.Core.Actions;
using HoodKeys.Core.Config;
using HoodKeys.Core.Dfly;
using HoodKeys.Core.Keys;
using static HoodKeys.Core.Tests.Actions.ConfigBuilder;

namespace HoodKeys.Core.Tests.Actions;

public class ActionDispatcherTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeKeySender _keys = new();
    private readonly FakeCommandRunner _commands = new();
    private readonly ListLogger<ActionDispatcher> _log = new();

    private ActionDispatcher Create(HoodKeysConfig config) => new(config, _keys, _commands, _log, _time);

    private static Di2Event Short(int channel) => new(channel, Di2EventKind.ShortPress);

    private static Di2Event Long(int channel) => new(channel, Di2EventKind.LongPress);

    private static Di2Event Release(int channel) => new(channel, Di2EventKind.Release);

    [Fact]
    public void Short_press_sends_the_configured_key()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), null))));

        dispatcher.Handle(Short(1));

        Assert.Equal([(ushort)'I'], _keys.Sent.Select(k => k.Key));
    }

    [Fact]
    public void Repeat_mode_repeats_the_short_action_at_the_interval_until_release()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), new LongPressConfig(null, 150)))));

        dispatcher.Handle(Short(1));
        dispatcher.Handle(Long(1));
        _time.Advance(TimeSpan.FromSeconds(1));
        var heldCount = _keys.Sent.Count;
        dispatcher.Handle(Release(1));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1 + 6, heldCount);
        Assert.Equal(heldCount, _keys.Sent.Count);
    }

    [Fact]
    public void Stopping_all_repeats_ends_every_running_repeat_and_logs_each_channel()
    {
        using var dispatcher = Create(Of(
            (1, new ChannelConfig(Key("I"), new LongPressConfig(null, 100))),
            (2, new ChannelConfig(Key("K"), new LongPressConfig(null, 100)))));

        dispatcher.Handle(Long(1));
        dispatcher.Handle(Long(2));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        var before = _keys.Sent.Count;
        dispatcher.StopAllRepeats();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, before);
        Assert.Equal(before, _keys.Sent.Count);
        Assert.Contains("Repeat stopped on channel 1 (link lost)", _log.Messages);
        Assert.Contains("Repeat stopped on channel 2 (link lost)", _log.Messages);
    }

    [Fact]
    public void Long_hold_events_do_nothing()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), new LongPressConfig(null, 150)))));

        dispatcher.Handle(new Di2Event(1, Di2EventKind.LongHold));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(_keys.Sent);
    }

    [Fact]
    public void Short_press_needs_no_release()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), null))));

        dispatcher.Handle(Short(1));
        dispatcher.Handle(Short(1));

        Assert.Equal(2, _keys.Sent.Count);
    }

    [Fact]
    public void Repeat_does_not_fire_before_the_first_interval()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), new LongPressConfig(null, 150)))));

        dispatcher.Handle(Long(1));
        _time.Advance(TimeSpan.FromMilliseconds(149));

        Assert.Empty(_keys.Sent);
    }

    [Fact]
    public void Repeat_on_one_channel_ignores_a_release_on_another()
    {
        using var dispatcher = Create(Of(
            (1, new ChannelConfig(Key("I"), new LongPressConfig(null, 100))),
            (2, new ChannelConfig(Key("K"), null))));

        dispatcher.Handle(Long(1));
        dispatcher.Handle(Release(2));
        _time.Advance(TimeSpan.FromMilliseconds(300));

        Assert.Equal(3, _keys.Sent.Count);
    }

    [Fact]
    public void Own_action_long_press_fires_once_and_does_not_repeat()
    {
        var config = Of((3, new ChannelConfig(Key("I"), new LongPressConfig(Key("F11"), null))));
        using var dispatcher = Create(config);

        dispatcher.Handle(Short(3));
        dispatcher.Handle(Long(3));
        _time.Advance(TimeSpan.FromSeconds(2));
        dispatcher.Handle(Release(3));

        Assert.Equal([(ushort)'I', (ushort)0x7A], _keys.Sent.Select(k => k.Key));
    }

    [Fact]
    public void Double_tap_sends_the_short_action_twice()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), null))));

        dispatcher.Handle(new Di2Event(1, Di2EventKind.DoubleTap));

        Assert.Equal([(ushort)'I', (ushort)'I'], _keys.Sent.Select(k => k.Key));
        Assert.Equal(2, _log.Messages.Count(m => m.StartsWith("Action sent for 1:DoubleTap: key I", StringComparison.Ordinal)));
    }

    [Fact]
    public void Double_tap_without_short_action_does_nothing()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(null, new LongPressConfig(Key("F11"), null)))));

        dispatcher.Handle(new Di2Event(1, Di2EventKind.DoubleTap));

        Assert.Empty(_keys.Sent);
        Assert.Empty(_commands.Started);
    }

    [Fact]
    public void Long_press_without_long_config_does_nothing()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), null))));

        dispatcher.Handle(Long(1));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(_keys.Sent);
    }

    [Fact]
    public async Task Slow_command_does_not_delay_the_next_key()
    {
        var config = Of(
            (1, new ChannelConfig(Command("Start-Sleep 10"), null)),
            (2, new ChannelConfig(Key("K"), null)));
        using var dispatcher = Create(config);

        var watch = Stopwatch.StartNew();
        dispatcher.Handle(Short(1));
        dispatcher.Handle(Short(2));
        watch.Stop();

        Assert.Equal(["Start-Sleep 10"], _commands.Started);
        Assert.Single(_keys.Sent);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"took {watch.Elapsed}");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Real_slow_powershell_command_returns_at_once_and_a_fast_one_creates_its_file()
    {
        var runner = new PowerShellRunner(new ListLogger<PowerShellRunner>());
        var file = Path.Combine(Path.GetTempPath(), $"hoodkeys-{Guid.NewGuid():N}.txt");
        try
        {
            var watch = Stopwatch.StartNew();
            var slow = runner.Start("Start-Sleep 3");
            var duration = watch.Elapsed;
            Assert.False(slow.IsCompleted);
            Assert.True(duration < TimeSpan.FromSeconds(1), $"Start took {duration}");

            var exit = await runner.Start($"New-Item -ItemType File -Path '{file}' | Out-Null").WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(0, exit);
            Assert.True(File.Exists(file));
            Assert.Equal(0, await slow.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Only_commands_written_in_the_config_are_run()
    {
        var config = Of(
            (1, new ChannelConfig(Command("Get-Date"), new LongPressConfig(Command("Get-Process"), null))),
            (2, new ChannelConfig(Key("K"), null)));
        using var dispatcher = Create(config);

        dispatcher.Handle(Short(1));
        dispatcher.Handle(Long(1));
        dispatcher.Handle(Short(2));
        dispatcher.Handle(Long(2));
        dispatcher.Handle(Short(3));
        dispatcher.Handle(Long(4));
        dispatcher.Handle(Release(1));

        Assert.Equal(["Get-Date", "Get-Process"], _commands.Started);
        Assert.Single(_keys.Sent);
    }

    [Fact]
    public void Events_for_unconfigured_channels_do_nothing()
    {
        using var dispatcher = Create(Of());

        dispatcher.Handle(Short(1));
        dispatcher.Handle(Long(2));

        Assert.Empty(_commands.Started);
        Assert.Empty(_keys.Sent);
    }

    [Fact]
    public void Log_has_event_received_and_action_sent_timestamps()
    {
        using var dispatcher = Create(Of((1, new ChannelConfig(Key("I"), null))));

        dispatcher.Handle(Short(1));

        Assert.Contains(_log.Messages, m => m.StartsWith("Event received 1:ShortPress at 2026-01-01T00:00:00", StringComparison.Ordinal));
        Assert.Contains(_log.Messages, m => m.StartsWith("Action sent for 1:ShortPress: key I received_at=2026-01-01T00:00:00", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failing_key_sender_does_not_stop_the_next_event()
    {
        var config = Of((1, new ChannelConfig(Key("I"), null)));
        var sender = new ThrowingOnceSender();
        using var dispatcher = new ActionDispatcher(config, sender, _commands, _log, _time);

        dispatcher.Handle(Short(1));
        dispatcher.Handle(Short(1));

        Assert.Equal(2, sender.Calls);
    }

    private sealed class ThrowingOnceSender : IKeySender
    {
        public int Calls { get; private set; }

        public void Send(KeyCombo combo)
        {
            if (++Calls == 1)
            {
                throw new InvalidOperationException("boom");
            }
        }
    }
}

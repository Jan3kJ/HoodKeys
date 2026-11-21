using HoodKeys.Core.Actions;
using HoodKeys.Core.Dfly;
using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Tests.Actions;

public class LatencyLogTests
{
    private sealed class SlowKeySender(FakeTimeProvider time, TimeSpan takes) : IKeySender
    {
        public void Send(KeyCombo combo) => time.Advance(takes);
    }

    [Fact]
    public void Key_send_logs_one_greppable_line_with_received_sent_and_latency()
    {
        var time = new FakeTimeProvider();
        var log = new ListLogger<ActionDispatcher>();
        var config = ConfigBuilder.Of((1, new(ConfigBuilder.Key("I"), null)));
        using var dispatcher = new ActionDispatcher(config, new SlowKeySender(time, TimeSpan.FromMilliseconds(7.5)), new FakeCommandRunner(), log, time);

        dispatcher.Handle(new Di2Event(1, Di2EventKind.ShortPress));

        var line = Assert.Single(log.Messages, m => m.Contains("latency_ms=", StringComparison.Ordinal));
        Assert.Equal(
            "Action sent for 1:ShortPress: key I received_at=2026-01-01T00:00:00.0000000+00:00 sent_at=2026-01-01T00:00:00.0075000+00:00 latency_ms=7.50",
            line);
    }

    [Fact]
    public void Command_send_logs_a_latency_line_too()
    {
        var time = new FakeTimeProvider();
        var log = new ListLogger<ActionDispatcher>();
        var config = ConfigBuilder.Of((2, new(ConfigBuilder.Command("Get-Date"), null)));
        using var dispatcher = new ActionDispatcher(config, new FakeKeySender(), new FakeCommandRunner(), log, time);

        dispatcher.Handle(new Di2Event(2, Di2EventKind.ShortPress));

        var line = Assert.Single(log.Messages, m => m.Contains("latency_ms=", StringComparison.Ordinal));
        Assert.Contains("command", line, StringComparison.Ordinal);
        Assert.EndsWith("latency_ms=0.00", line, StringComparison.Ordinal);
    }
}

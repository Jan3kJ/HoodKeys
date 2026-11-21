using HoodKeys.Core.Dfly;

namespace HoodKeys.Core.Tests.Dfly;

public class DflyDecoderTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> FixtureFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(FixtureDir, "*.txt")
                     .Where(f => Path.GetFileName(f) is not (B1Log or C2Monitor))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    private const string B1Log = "b1-log.txt";
    private const string C2Monitor = "c2-monitor.txt";

    [Fact]
    public void Fixture_set_covers_every_case()
    {
        var names = Directory.GetFiles(FixtureDir, "*.txt")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet();

        string[] required =
        [
            "idle", "short", "long", "double-tap", "short-then-long", "multi-channel",
            "counter-wrap", "report-counter", "spec-examples", "duplicates", "unknown-nibble",
            "odd-lengths", "b1-log", "c2-monitor",
        ];

        Assert.Empty(required.Except(names));
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixture_produces_expected_events(string fixture)
    {
        var decoder = new DflyDecoder();
        var lines = File.ReadAllLines(Path.Combine(FixtureDir, fixture));

        foreach (var line in lines.Where(l => l.Trim().Length > 0 && !l.StartsWith('#')))
        {
            var parts = line.Split("=>");
            var report = ParseHex(parts[0]);
            var expected = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseEvent).ToArray();

            var actual = decoder.Decode(report);

            Assert.True(
                expected.SequenceEqual(actual),
                $"{fixture}: report '{parts[0].Trim()}' expected [{string.Join(", ", expected.AsEnumerable())}] but got [{string.Join(", ", actual)}]");
        }
    }

    [Fact]
    public void B1_log_replay_gives_the_full_expected_event_list()
    {
        var actual = Replay(B1Log);

        // The first report (channel 4 = 11) is a press like any other.
        string[] expected =
        [
            // channel 4: short (first report), short, long, hold, release, long, hold, hold, release
            "4:ShortPress", "4:ShortPress", "4:ShortPress", "4:LongPress", "4:LongHold", "4:Release",
            "4:LongPress", "4:LongHold", "4:LongHold", "4:Release",

            // channel 3: short x3, long, hold, release, long, hold, release
            "3:ShortPress", "3:ShortPress", "3:ShortPress", "3:LongPress", "3:LongHold", "3:Release",
            "3:LongPress", "3:LongHold", "3:Release",

            // channel 2: long, release, short x2, long, release, long, release
            "2:LongPress", "2:Release", "2:ShortPress", "2:ShortPress", "2:LongPress", "2:Release",
            "2:LongPress", "2:Release",

            // channel 1: short x3, long, release, long, hold, release
            "1:ShortPress", "1:ShortPress", "1:ShortPress", "1:LongPress", "1:Release",
            "1:LongPress", "1:LongHold", "1:Release",

            // channel 3 again: two shorts
            "3:ShortPress", "3:ShortPress",
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Null_report_gives_no_events()
    {
        Assert.Empty(new DflyDecoder().Decode(null));
    }

    [Fact]
    public void C2_monitor_replay_gives_the_full_expected_event_list()
    {
        // Log line 16 (first report, 20 F0 F0 F0 11) is a real press on channel 4.
        string[] expected =
        [
            // channel 4: short (first report), short, long, release
            "4:ShortPress", "4:ShortPress", "4:LongPress", "4:Release",

            // channel 3: short, short, long, release
            "3:ShortPress", "3:ShortPress", "3:LongPress", "3:Release",

            // channel 2: long, release, short, short, long, release
            "2:LongPress", "2:Release", "2:ShortPress", "2:ShortPress", "2:LongPress", "2:Release",

            // channel 1: short, long, release, long, release, short, long, hold x4, release
            "1:ShortPress", "1:LongPress", "1:Release", "1:LongPress", "1:Release",
            "1:ShortPress", "1:LongPress", "1:LongHold", "1:LongHold", "1:LongHold", "1:LongHold", "1:Release",

            // channel 4: long, hold, hold, release, short
            "4:LongPress", "4:LongHold", "4:LongHold", "4:Release", "4:ShortPress",

            // channel 2: 47 double tap, short, short, 4A and 4B double taps, short x3, 4F double tap, short
            "2:DoubleTap", "2:ShortPress", "2:ShortPress", "2:DoubleTap", "2:DoubleTap",
            "2:ShortPress", "2:ShortPress", "2:ShortPress", "2:DoubleTap", "2:ShortPress",
        ];

        Assert.Equal(expected, Replay(C2Monitor));
    }

    [Fact]
    public void First_report_is_decoded_like_any_other()
    {
        var decoder = new DflyDecoder();

        Assert.Equal(
            [new Di2Event(1, Di2EventKind.ShortPress), new Di2Event(4, Di2EventKind.Release)],
            decoder.Decode([0x20, 0x11, 0xF0, 0xF0, 0x06]));
        Assert.Empty(new DflyDecoder().Decode([0x20, 0xF0, 0xF0, 0xF0, 0xF0]));
    }

    [Fact]
    public void Reset_returns_every_channel_to_idle()
    {
        var decoder = new DflyDecoder();
        decoder.Decode([0x20, 0x11, 0xF0, 0xF0, 0xF0]);
        Assert.Empty(decoder.Decode([0x21, 0x11, 0xF0, 0xF0, 0xF0]));
        decoder.Reset();

        Assert.Equal([new Di2Event(1, Di2EventKind.ShortPress)], decoder.Decode([0x20, 0x11, 0xF0, 0xF0, 0xF0]));
    }

    [Fact]
    public void High_nibble_4_is_a_double_tap()
    {
        var decoder = new DflyDecoder();
        decoder.Decode([0x20, 0xF0, 0x06, 0xF0, 0xF0]);

        Assert.Equal([new Di2Event(2, Di2EventKind.DoubleTap)], decoder.Decode([0x21, 0xF0, 0x47, 0xF0, 0xF0]));
    }

    private static string[] Replay(string fixture)
    {
        var decoder = new DflyDecoder();
        return File.ReadAllLines(Path.Combine(FixtureDir, fixture))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith('#'))
            .SelectMany(l => decoder.Decode(ParseHex(l[(l.IndexOf(' ') + 1)..])))
            .Select(e => e.ToString())
            .ToArray();
    }

    private static byte[] ParseHex(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();

    private static Di2Event ParseEvent(string text)
    {
        var parts = text.Split(':');
        return new Di2Event(int.Parse(parts[0]), Enum.Parse<Di2EventKind>(parts[1]));
    }
}

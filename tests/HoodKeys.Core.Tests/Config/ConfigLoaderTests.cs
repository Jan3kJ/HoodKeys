using HoodKeys.Core.Config;

namespace HoodKeys.Core.Tests.Config;

public class ConfigLoaderTests
{
    private const string File = "test.json";

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Samples", "mywhoosh.json");

    [Fact]
    public void Sample_mywhoosh_loads()
    {
        var result = ConfigLoader.Load(SamplePath);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var config = result.Config!;
        Assert.Equal([1, 2], config.Channels.Keys.OrderBy(k => k));
        Assert.Equal("I", config.Channels[1].Short!.KeyText);
        Assert.Equal((ushort)'I', config.Channels[1].Short!.Keys!.Key);
        Assert.Equal((ushort)'K', config.Channels[2].Short!.Keys!.Key);
        Assert.True(config.Channels[1].Long!.IsRepeat);
        Assert.Equal(150, config.Channels[2].Long!.RepeatIntervalMs);
        Assert.Null(config.Device);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var result = ConfigLoader.Parse("""
            { // c
              "channels": { "3": { "short": { "key": "Ctrl+Shift+X", }, }, },
            }
            """, File);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Device_name_and_address_are_read()
    {
        var result = ConfigLoader.Parse("""{ "device": { "name": "RD", "address": "AA:BB" } }""", File);

        Assert.Equal(new DeviceConfig("RD", "AA:BB"), result.Config!.Device);
    }

    [Fact]
    public void Repeat_interval_defaults_to_150()
    {
        var result = ConfigLoader.Parse("""{ "channels": { "1": { "short": { "key": "I" }, "long": "repeat" } } }""", File);

        Assert.Equal(150, result.Config!.Channels[1].Long!.RepeatIntervalMs);
    }

    [Fact]
    public void Long_can_be_its_own_command_action()
    {
        var result = ConfigLoader.Parse("""{ "channels": { "1": { "long": { "command": "Get-Date" } } } }""", File);

        var longPress = result.Config!.Channels[1].Long!;
        Assert.False(longPress.IsRepeat);
        Assert.Equal("Get-Date", longPress.Action!.Command);
    }

    [Theory]
    [InlineData("""{ "channels": { "1": { "short": { "key": "Banana" } } } }""", "channels.1.short.key")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "Ctrl+Banana" } } } }""", "channels.1.short.key")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "Ctrl" } } } }""", "channels.1.short.key")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "" } } } }""", "channels.1.short.key")]
    [InlineData("""{ "channels": { "5": { "short": { "key": "I" } } } }""", "channels.5")]
    [InlineData("""{ "channels": { "0": { "short": { "key": "I" } } } }""", "channels.0")]
    [InlineData("""{ "channels": { "x": {} } }""", "channels.x")]
    [InlineData("""{ "channels": { "2": { "short": { "command": "" } } } }""", "channels.2.short.command")]
    [InlineData("""{ "channels": { "2": { "short": { "command": "  " } } } }""", "channels.2.short.command")]
    [InlineData("""{ "channels": { "2": { "long": { "command": "" } } } }""", "channels.2.long.command")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "I", "command": "x" } } } }""", "channels.1.short")]
    [InlineData("""{ "channels": { "1": { "short": {} } } }""", "channels.1.short")]
    [InlineData("""{ "channels": { "1": { "short": { "key": 5 } } } }""", "channels.1.short.key")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "I" }, "long": "hold" } } }""", "channels.1.long")]
    [InlineData("""{ "channels": { "1": { "long": "repeat" } } }""", "channels.1.long")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "I" }, "long": "repeat", "repeatIntervalMs": 0 } } }""", "channels.1.repeatIntervalMs")]
    [InlineData("""{ "channels": { "1": { "short": { "key": "I" }, "repeatIntervalMs": 100 } } }""", "channels.1.repeatIntervalMs")]
    [InlineData("""{ "channels": { "1": { "shrt": { "key": "I" } } } }""", "channels.1.shrt")]
    [InlineData("""{ "chanels": {} }""", "chanels")]
    [InlineData("""{ "device": { "nam": "x" } }""", "device.nam")]
    public void Invalid_config_names_file_and_field(string json, string expectedPath)
    {
        var result = ConfigLoader.Parse(json, File);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.Path == expectedPath);
        Assert.Equal(File, error.File);
        Assert.StartsWith($"{File}: {expectedPath}: ", error.ToString());
    }

    [Fact]
    public void All_errors_are_reported_not_just_the_first()
    {
        var result = ConfigLoader.Parse("""
            { "channels": { "5": {}, "1": { "short": { "key": "Banana" } }, "2": { "short": { "command": "" } } } }
            """, File);

        Assert.Equal(
            ["channels.5", "channels.1.short.key", "channels.2.short.command"],
            result.Errors.Select(e => e.Path));
    }

    [Fact]
    public void Malformed_json_is_reported_with_the_file()
    {
        var result = ConfigLoader.Parse("{ nope", File);

        var error = Assert.Single(result.Errors);
        Assert.Equal(File, error.File);
        Assert.Contains("invalid JSON", error.Message);
    }

    [Fact]
    public void Missing_file_is_reported_with_the_path()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "does-not-exist.json");

        var result = ConfigLoader.Load(path);

        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
    }

    [Fact]
    public void Bad_file_on_disk_reports_its_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hoodkeys-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, """{ "channels": { "5": {} } }""");
        try
        {
            var result = ConfigLoader.Load(path);

            Assert.StartsWith($"{path}: channels.5: ", Assert.Single(result.Errors).ToString());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Default_path_is_under_appdata()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoodKeys", "config.json");

        Assert.Equal(expected, ConfigPaths.DefaultPath());
    }
}

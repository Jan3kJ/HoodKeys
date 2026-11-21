using HoodKeys.Core.Ble;

namespace HoodKeys.Core.Tests.Ble;

public class BleAddressTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", 0xAABBCCDDEEFFUL)]
    [InlineData("aa-bb-cc-dd-ee-ff", 0xAABBCCDDEEFFUL)]
    [InlineData("0011223344ff", 0x0011223344FFUL)]
    [InlineData(" 00:00:00:00:00:01 ", 1UL)]
    public void Parses_valid_addresses(string text, ulong expected)
    {
        Assert.True(BleAddress.TryParse(text, out var address));
        Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    [InlineData("AA:BBCCDDEEFF")]
    [InlineData("GG:BB:CC:DD:EE:FF")]
    public void Rejects_invalid_addresses(string? text) =>
        Assert.False(BleAddress.TryParse(text, out _));

    [Fact]
    public void Formats_as_six_upper_case_pairs() =>
        Assert.Equal("0A:0B:0C:0D:0E:0F", BleAddress.Format(0x0A0B0C0D0E0FUL));
}

public class AdvertisedDeviceTests
{
    [Fact]
    public void Dfly_service_marks_device_as_di2()
    {
        var device = new AdvertisedDevice(1, null, -60, [DflyGatt.ServiceUuid]);

        Assert.True(device.AdvertisesDfly);
        Assert.True(device.LooksLikeDi2);
    }

    [Theory]
    [InlineData("SHIMANO RD-RX827", true)]
    [InlineData("Di2 unit", true)]
    [InlineData("EW-EN100", true)]
    [InlineData("Forerunner 255", false)]
    [InlineData(null, false)]
    public void Name_heuristic(string? name, bool expected) =>
        Assert.Equal(expected, new AdvertisedDevice(1, name, -60, []).LooksLikeDi2);

    [Fact]
    public void Raw_report_hex_matches_fixture_format() =>
        Assert.Equal("20 F0 10 F0 F0", new RawReport(DateTimeOffset.Now, "indicate", [0x20, 0xF0, 0x10, 0xF0, 0xF0]).Hex);
}

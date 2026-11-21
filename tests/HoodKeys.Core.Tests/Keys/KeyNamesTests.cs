using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Tests.Keys;

public class KeyNamesTests
{
    [Theory]
    [InlineData("I", 0x49)]
    [InlineData("k", 0x4B)]
    [InlineData("7", 0x37)]
    [InlineData("F12", 0x7B)]
    [InlineData("Space", 0x20)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Numpad5", 0x65)]
    public void Single_keys_map_to_virtual_key_codes(string text, int vk)
    {
        var combo = KeyNames.TryParse(text, out var error);

        Assert.Null(error);
        Assert.Empty(combo!.Modifiers);
        Assert.Equal((ushort)vk, combo.Key);
    }

    [Fact]
    public void Combo_keeps_modifiers_in_order()
    {
        var combo = KeyNames.TryParse("Ctrl+Shift+X", out _);

        Assert.Equal([KeyNames.Ctrl, KeyNames.Shift], combo!.Modifiers);
        Assert.Equal((ushort)'X', combo.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+X")]
    [InlineData("X+Ctrl")]
    [InlineData("Banana")]
    [InlineData("F25")]
    public void Invalid_key_strings_are_rejected_with_a_message(string text)
    {
        var combo = KeyNames.TryParse(text, out var error);

        Assert.Null(combo);
        Assert.False(string.IsNullOrEmpty(error));
    }
}

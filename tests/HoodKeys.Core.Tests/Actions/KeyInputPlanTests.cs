using HoodKeys.Core.Actions;
using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Tests.Actions;

public class KeyInputPlanTests
{
    [Fact]
    public void Single_key_is_down_then_up()
    {
        var combo = KeyNames.TryParse("F11", out _)!;

        var steps = KeyInputPlan.Build(combo);

        Assert.Equal([new KeyStep(0x7A, false), new KeyStep(0x7A, true)], steps);
    }

    [Fact]
    public void Combo_holds_modifiers_around_the_key()
    {
        var combo = KeyNames.TryParse("Ctrl+Shift+X", out var error)!;

        var steps = KeyInputPlan.Build(combo);

        Assert.Null(error);
        Assert.Equal(
            [
                new KeyStep(KeyNames.Ctrl, false),
                new KeyStep(KeyNames.Shift, false),
                new KeyStep((ushort)'X', false),
                new KeyStep((ushort)'X', true),
                new KeyStep(KeyNames.Shift, true),
                new KeyStep(KeyNames.Ctrl, true),
            ],
            steps);
    }

    [Theory]
    [InlineData(" ctrl + alt + Delete ", 2, 0x2E)]
    [InlineData("I", 0, 0x49)]
    [InlineData("F11", 0, 0x7A)]
    public void Key_strings_parse(string text, int modifierCount, int key)
    {
        var combo = KeyNames.TryParse(text, out var error);

        Assert.Null(error);
        Assert.Equal(modifierCount, combo!.Modifiers.Count);
        Assert.Equal((ushort)key, combo.Key);
    }
}

using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Actions;

/// <summary>One key transition in a key press sequence.</summary>
public readonly record struct KeyStep(ushort VirtualKey, bool Up);

/// <summary>The ordered key transitions for a combo: modifiers down, key down, key up, modifiers up in reverse.</summary>
public static class KeyInputPlan
{
    public static IReadOnlyList<KeyStep> Build(KeyCombo combo)
    {
        var steps = new List<KeyStep>(combo.Modifiers.Count * 2 + 2);
        foreach (var modifier in combo.Modifiers)
        {
            steps.Add(new KeyStep(modifier, false));
        }

        steps.Add(new KeyStep(combo.Key, false));
        steps.Add(new KeyStep(combo.Key, true));
        for (var i = combo.Modifiers.Count - 1; i >= 0; i--)
        {
            steps.Add(new KeyStep(combo.Modifiers[i], true));
        }

        return steps;
    }
}

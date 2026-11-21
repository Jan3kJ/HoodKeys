namespace HoodKeys.Core.Keys;

/// <summary>A parsed key string: modifier virtual-key codes (in written order) plus one main key.</summary>
public sealed record KeyCombo(IReadOnlyList<ushort> Modifiers, ushort Key);

/// <summary>Table of key names and their Windows virtual-key codes. Names are case-insensitive.</summary>
public static class KeyNames
{
    public const ushort Shift = 0x10;
    public const ushort Ctrl = 0x11;
    public const ushort Alt = 0x12;
    public const ushort Win = 0x5B;

    private static readonly Dictionary<string, ushort> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = Ctrl,
        ["Control"] = Ctrl,
        ["Shift"] = Shift,
        ["Alt"] = Alt,
        ["Win"] = Win,
    };

    private static readonly Dictionary<string, ushort> Keys = BuildKeys();

    /// <summary>Looks up a single key or modifier name.</summary>
    public static bool TryGetVirtualKey(string name, out ushort virtualKey) =>
        Keys.TryGetValue(name, out virtualKey) || Modifiers.TryGetValue(name, out virtualKey);

    public static bool IsModifier(string name) => Modifiers.ContainsKey(name);

    /// <summary>
    /// Parses "I" or "Ctrl+Shift+X": zero or more distinct modifiers followed by exactly one non-modifier key.
    /// Returns null and an error message when the text is not valid.
    /// </summary>
    public static KeyCombo? TryParse(string? text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "key name is empty";
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = new List<ushort>();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!Modifiers.TryGetValue(parts[i], out var modifier))
            {
                error = $"'{parts[i]}' is not a modifier (Ctrl, Shift, Alt, Win)";
                return null;
            }

            if (modifiers.Contains(modifier))
            {
                error = $"modifier '{parts[i]}' is repeated";
                return null;
            }

            modifiers.Add(modifier);
        }

        var last = parts[^1];
        if (!Keys.TryGetValue(last, out var key))
        {
            error = last.Length == 0
                ? "key name is missing after '+'"
                : $"unknown key name '{last}'";
            return null;
        }

        return new KeyCombo(modifiers, key);
    }

    private static Dictionary<string, ushort> BuildKeys()
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            keys[c.ToString()] = c;
        }

        for (var c = '0'; c <= '9'; c++)
        {
            keys[c.ToString()] = c;
        }

        for (var i = 1; i <= 24; i++)
        {
            keys[$"F{i}"] = (ushort)(0x6F + i);
        }

        for (var i = 0; i <= 9; i++)
        {
            keys[$"Numpad{i}"] = (ushort)(0x60 + i);
        }

        var named = new (string Name, ushort Code)[]
        {
            ("Backspace", 0x08), ("Tab", 0x09), ("Enter", 0x0D), ("Return", 0x0D), ("Escape", 0x1B),
            ("Esc", 0x1B), ("Space", 0x20), ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23),
            ("Home", 0x24), ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
            ("Insert", 0x2D), ("Delete", 0x2E), ("NumpadMultiply", 0x6A), ("NumpadAdd", 0x6B),
            ("NumpadSubtract", 0x6D), ("NumpadDecimal", 0x6E), ("NumpadDivide", 0x6F),
            ("Semicolon", 0xBA), ("Equals", 0xBB), ("Comma", 0xBC), ("Minus", 0xBD),
            ("Period", 0xBE), ("Slash", 0xBF), ("Backtick", 0xC0), ("LeftBracket", 0xDB),
            ("Backslash", 0xDC), ("RightBracket", 0xDD), ("Quote", 0xDE),
            ("VolumeMute", 0xAD), ("VolumeDown", 0xAE), ("VolumeUp", 0xAF),
            ("MediaNext", 0xB0), ("MediaPrevious", 0xB1), ("MediaStop", 0xB2), ("MediaPlayPause", 0xB3),
        };
        foreach (var (name, code) in named)
        {
            keys[name] = code;
        }

        return keys;
    }
}

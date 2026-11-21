using System.Globalization;
using System.Text.Json;
using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Config;

/// <summary>Reads and validates the JSON config. Comments and trailing commas are allowed.</summary>
public static class ConfigLoader
{
    public const int DefaultRepeatIntervalMs = 150;
    public const int MinChannel = 1;
    public const int MaxChannel = 4;

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads <paramref name="path"/>, or the default path when null.</summary>
    public static ConfigLoadResult Load(string? path = null)
    {
        path ??= ConfigPaths.DefaultPath();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(path, string.Empty, $"cannot read file: {ex.Message}");
        }

        return Parse(text, path);
    }

    /// <summary>Validates config text; <paramref name="file"/> is only used to name the file in errors.</summary>
    public static ConfigLoadResult Parse(string json, string file)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : string.Empty;
            return Fail(file, string.Empty, $"invalid JSON{where}: {ex.Message}");
        }

        using (doc)
        {
            var errors = new List<ConfigError>();
            var config = ReadRoot(doc.RootElement, file, errors);
            return errors.Count == 0 ? new ConfigLoadResult(config, errors) : new ConfigLoadResult(null, errors);
        }
    }

    private static ConfigLoadResult Fail(string file, string path, string message) =>
        new(null, [new ConfigError(file, path, message)]);

    private static HoodKeysConfig? ReadRoot(JsonElement root, string file, List<ConfigError> errors)
    {
        if (!RequireObject(root, file, string.Empty, errors))
        {
            return null;
        }

        DeviceConfig? device = null;
        var channels = new Dictionary<int, ChannelConfig>();
        foreach (var prop in root.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "device":
                    device = ReadDevice(prop.Value, file, errors);
                    break;
                case "channels":
                    ReadChannels(prop.Value, file, channels, errors);
                    break;
                default:
                    errors.Add(new ConfigError(file, prop.Name, "unknown field"));
                    break;
            }
        }

        return new HoodKeysConfig(device, channels);
    }

    private static DeviceConfig? ReadDevice(JsonElement element, string file, List<ConfigError> errors)
    {
        if (!RequireObject(element, file, "device", errors))
        {
            return null;
        }

        string? name = null;
        string? address = null;
        foreach (var prop in element.EnumerateObject())
        {
            var path = $"device.{prop.Name}";
            switch (prop.Name)
            {
                case "name":
                    name = ReadString(prop.Value, file, path, errors);
                    break;
                case "address":
                    address = ReadString(prop.Value, file, path, errors);
                    break;
                default:
                    errors.Add(new ConfigError(file, path, "unknown field"));
                    break;
            }
        }

        return new DeviceConfig(name, address);
    }

    private static void ReadChannels(JsonElement element, string file, Dictionary<int, ChannelConfig> channels, List<ConfigError> errors)
    {
        if (!RequireObject(element, file, "channels", errors))
        {
            return;
        }

        foreach (var prop in element.EnumerateObject())
        {
            var path = $"channels.{prop.Name}";
            if (!int.TryParse(prop.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number < MinChannel || number > MaxChannel)
            {
                errors.Add(new ConfigError(file, path, $"channel must be {MinChannel} to {MaxChannel}"));
                continue;
            }

            var channel = ReadChannel(prop.Value, file, path, errors);
            if (channel is not null)
            {
                channels[number] = channel;
            }
        }
    }

    private static ChannelConfig? ReadChannel(JsonElement element, string file, string path, List<ConfigError> errors)
    {
        if (!RequireObject(element, file, path, errors))
        {
            return null;
        }

        ActionConfig? shortAction = null;
        JsonElement? longElement = null;
        int? interval = null;
        foreach (var prop in element.EnumerateObject())
        {
            var fieldPath = $"{path}.{prop.Name}";
            switch (prop.Name)
            {
                case "short":
                    shortAction = ReadAction(prop.Value, file, fieldPath, errors);
                    break;
                case "long":
                    longElement = prop.Value;
                    break;
                case "repeatIntervalMs":
                    interval = ReadInterval(prop.Value, file, fieldPath, errors);
                    break;
                default:
                    errors.Add(new ConfigError(file, fieldPath, "unknown field"));
                    break;
            }
        }

        var isRepeat = longElement is { ValueKind: JsonValueKind.String } text && text.GetString() == "repeat";
        if (interval is not null && !isRepeat)
        {
            errors.Add(new ConfigError(file, $"{path}.repeatIntervalMs", "only valid when long is \"repeat\""));
        }

        LongPressConfig? longPress = null;
        if (longElement is { } lng)
        {
            var longPath = $"{path}.long";
            if (isRepeat)
            {
                if (shortAction is null)
                {
                    errors.Add(new ConfigError(file, longPath, "\"repeat\" needs a short action to repeat"));
                }

                longPress = new LongPressConfig(null, interval ?? DefaultRepeatIntervalMs);
            }
            else if (lng.ValueKind == JsonValueKind.String)
            {
                errors.Add(new ConfigError(file, longPath, $"expected an action object or \"repeat\", got \"{lng.GetString()}\""));
            }
            else
            {
                var action = ReadAction(lng, file, longPath, errors);
                if (action is not null)
                {
                    longPress = new LongPressConfig(action, null);
                }
            }
        }

        return new ChannelConfig(shortAction, longPress);
    }

    private static int? ReadInterval(JsonElement element, string file, string path, List<ConfigError> errors)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value > 0)
        {
            return value;
        }

        errors.Add(new ConfigError(file, path, "expected a positive whole number of milliseconds"));
        return null;
    }

    private static ActionConfig? ReadAction(JsonElement element, string file, string path, List<ConfigError> errors)
    {
        if (!RequireObject(element, file, path, errors))
        {
            return null;
        }

        var hasKey = false;
        var hasCommand = false;
        string? keyText = null;
        string? command = null;
        var ok = true;
        foreach (var prop in element.EnumerateObject())
        {
            var fieldPath = $"{path}.{prop.Name}";
            switch (prop.Name)
            {
                case "key":
                    hasKey = true;
                    keyText = ReadString(prop.Value, file, fieldPath, errors);
                    ok &= keyText is not null;
                    break;
                case "command":
                    hasCommand = true;
                    command = ReadString(prop.Value, file, fieldPath, errors);
                    ok &= command is not null;
                    break;
                default:
                    errors.Add(new ConfigError(file, fieldPath, "unknown field"));
                    ok = false;
                    break;
            }
        }

        if (hasKey && hasCommand)
        {
            errors.Add(new ConfigError(file, path, "an action has either key or command, not both"));
            return null;
        }

        if (!hasKey && !hasCommand)
        {
            errors.Add(new ConfigError(file, path, "an action needs key or command"));
            return null;
        }

        if (!ok)
        {
            return null;
        }

        if (hasCommand)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                errors.Add(new ConfigError(file, $"{path}.command", "command is empty"));
                return null;
            }

            return new ActionConfig(null, command);
        }

        var combo = KeyNames.TryParse(keyText, out var error);
        if (combo is null)
        {
            errors.Add(new ConfigError(file, $"{path}.key", error!));
            return null;
        }

        return new ActionConfig(combo, null) { KeyText = keyText };
    }

    private static string? ReadString(JsonElement element, string file, string path, List<ConfigError> errors)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        errors.Add(new ConfigError(file, path, "expected a string"));
        return null;
    }

    private static bool RequireObject(JsonElement element, string file, string path, List<ConfigError> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        errors.Add(new ConfigError(file, path, "expected an object"));
        return false;
    }
}

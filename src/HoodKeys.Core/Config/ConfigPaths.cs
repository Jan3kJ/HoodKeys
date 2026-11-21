namespace HoodKeys.Core.Config;

public static class ConfigPaths
{
    /// <summary>%APPDATA%\HoodKeys\config.json.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoodKeys", "config.json");
}

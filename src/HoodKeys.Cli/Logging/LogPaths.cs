namespace HoodKeys.Cli.Logging;

public static class LogPaths
{
    /// <summary>%LOCALAPPDATA%\HoodKeys\logs.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoodKeys", "logs");

    /// <summary>The log file of the local day <paramref name="localNow"/> falls on: <c>hoodkeys-YYYY-MM-DD.log</c>.</summary>
    public static string FileFor(string directory, DateTimeOffset localNow) =>
        Path.Combine(directory, $"hoodkeys-{localNow:yyyy-MM-dd}.log");
}
